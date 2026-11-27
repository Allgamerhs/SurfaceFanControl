using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SurfaceProFanControl.Core;
using SurfaceProFanControl.Infrastructure;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SurfaceProFanControl
{
    public partial class MainWindow : Window
    {
        private const int WmHotKey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const int HotKeyAutomatic = 101;
        private const int HotKeyQuiet = 102;
        private const int HotKeyBalanced = 103;
        private const int HotKeyMaximum = 104;
        private const int EcMeasurementIntervalTicks = 5;
        private static readonly TimeSpan EcSampleStaleAfter = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan OperatorWaitForPoll = TimeSpan.FromSeconds(2);

        private readonly FanGuardClient _guard;
        private readonly TelemetryService _telemetry = new TelemetryService();
        private readonly TelemetryHistoryStore _history = new TelemetryHistoryStore();
        private readonly UserSettingsStore _settingsStore = new UserSettingsStore();
        private UserSettings _settings;
        private readonly HardwareSupport _hardwareSupport;
        private readonly DispatcherTimer _timer;
        private readonly List<(DateTime TimeUtc, double? Celsius)> _temperatureHistory = new List<(DateTime, double?)>();
        private DateTime? _ecTemperatureTimeUtc;
        private DateTime? _lastSafetySampleUtc;
        private readonly TemperatureSignalTracker _temperatureSignalTracker = new TemperatureSignalTracker();
        private readonly ThermalSafetyController _emergencyController = new ThermalSafetyController(82, 74, 3);
        private readonly DateTime _profileArmUtc;
        private TelemetrySnapshot? _lastRawTelemetry;
        private int? _peakFan0Rpm;
        private int? _peakFan1Rpm;
        private FanGuardSnapshot? _lastRpmMeasurement;
        private FanGuardSnapshot? _lastLoggedMeasurement;
        private int _loggedAttachmentMeasurements;
        private bool _logResumedProfileMeasurements;
        private TemperatureSignal _lastTemperatureSignal = TemperatureSignal.Empty;
        private double? _lastEcCriticalCelsius;
        private DateTime _modeChangedUtc = DateTime.UtcNow;
        private DateTime? _boostEndsUtc;
        private bool _lastKnownMaximum;
        private bool _fanPolicyActive;
        private int _policySelectionVersion;
        private bool _requestInFlight;
        private Task<bool>? _activePoll;
        private bool _closing;
        private bool _allowClose;
        private bool _loadingSettings;
        private bool _emergencyNotificationShown;
        private int _timerTicks;
        private HwndSource? _windowSource;
        private Forms.NotifyIcon? _trayIcon;
        private Drawing.Icon? _trayDrawingIcon;

        public MainWindow(DeviceSnapshot device, HardwareSupport hardwareSupport, bool startupLaunch = false)
            : this(device, hardwareSupport, startupLaunch, null)
        {
        }

        public MainWindow(DeviceSnapshot device, HardwareSupport hardwareSupport, bool startupLaunch, FanGuardClient? existingGuard)
        {
            _hardwareSupport = hardwareSupport;
            _guard = existingGuard ?? new FanGuardClient();
            // The running service keeps its current policy until the operator chooses a fan setting.
            _fanPolicyActive = existingGuard == null;
            _settings = _settingsStore.Load();
            if (string.IsNullOrWhiteSpace(_settings.LanguageCode))
            {
                _settings.LanguageCode = LocalizationCatalog.NormalizeLanguageCode(CultureInfo.CurrentUICulture.Name);
            }
            LocalizationService.Use(_settings.LanguageCode);

            _loadingSettings = true;
            try
            {
                InitializeComponent();
            }
            finally
            {
                _loadingSettings = false;
            }
            DeviceText.Text = device.ComputerName + " • " + device.Identity.Model;
            _profileArmUtc = DateTime.UtcNow.AddSeconds(startupLaunch ? _settings.StartupDelaySeconds : 0);
            ApplySettingsToControls();
            ApplyLocalization();
            InitializeTrayIcon();

            if (startupLaunch && _settings.StartMinimized)
            {
                WindowState = WindowState.Minimized;
            }

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            Loaded += Window_Loaded;
            SourceInitialized += Window_SourceInitialized;
        }

        private string T(string key) => LocalizationService.Text(key);

        internal void ResumeSavedProfile(bool logMeasurements)
        {
            _logResumedProfileMeasurements = logMeasurements;
            _fanPolicyActive = true;
        }

        private void ApplySettingsToControls()
        {
            _loadingSettings = true;
            try
            {
                AggressivenessSlider.Value = _settings.CoolingAggressivenessPercent;
                AggressivenessTextBox.Text = _settings.CoolingAggressivenessPercent.ToString(CultureInfo.CurrentCulture);
                AcAggressivenessSlider.Value = _settings.AcAggressivenessPercent;
                BatteryAggressivenessSlider.Value = _settings.BatteryAggressivenessPercent;
                UsePowerProfilesCheckBox.IsChecked = _settings.UsePowerProfiles;
                StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
                AlwaysOnTopCheckBox.IsChecked = _settings.AlwaysOnTop;
                CloseToTrayCheckBox.IsChecked = _settings.CloseToTray;
                NotificationsCheckBox.IsChecked = _settings.NotificationsEnabled;
                Topmost = _settings.AlwaysOnTop;
                SelectBoostMinutes(_settings.BoostMinutes);
                SelectStartupDelay(_settings.StartupDelaySeconds);
                SelectCurveDwell(_settings.CurveDwellSeconds);
                CustomCurveEnabledCheckBox.IsChecked = _settings.CustomCurveEnabled;
                PredictiveCoolingCheckBox.IsChecked = _settings.PredictiveCoolingEnabled;
                CurveTurnOnSlider.Value = _settings.CurveTurnOnCelsius;
                CurveTurnOffSlider.Value = _settings.CurveTurnOffCelsius;
                PopulateLanguages();
                PopulateProfiles();
                UpdatePowerProfileControls();
                UpdateCurveControls();

                try
                {
                    StartWithWindowsCheckBox.IsChecked = StartupRegistration.IsEnabled();
                }
                catch (Exception ex)
                {
                    StartWithWindowsCheckBox.IsChecked = false;
                    StatusText.Text = ex.Message;
                    AppLogger.Error("Windows startup registration could not be read.", ex);
                }
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void ApplyLocalization()
        {
            LocalizationService.Use(_settings.LanguageCode);
            var language = LocalizationService.CurrentLanguage;
            FlowDirection = language.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            FontFamily = new FontFamily(language.FontFamily);
            Title = T("AppTitle");
            HeaderText.Text = T("Header");
            FanControlTab.Header = T("TabControl");
            CurveTab.Header = T("TabCurve");
            ProfilesTab.Header = T("TabProfiles");
            StatusTab.Header = T("TabStatus");
            SettingsTab.Header = T("TabSettings");
            TemperatureLabel.Text = T("Temperature");
            FanCommandLabel.Text = T("FanCommand");
            CpuPowerLabel.Text = T("CpuPower");
            ProfileLabel.Text = T("Profile");
            AggressivenessLabel.Text = T("Aggressiveness");
            VerifiedPeakLabel.Text = T("VerifiedPeakRpm") + ":";
            TimedBoostButton.Content = T("TimedBoost");
            ConstantMaximumButton.Content = T("ConstantMaximum");
            AutomaticButton.Content = T("Automatic");
            CustomCurveEnabledCheckBox.Content = T("CurveEnabled");
            CurveTurnOnLabel.Text = T("CurveTurnOn");
            CurveTurnOffLabel.Text = T("CurveTurnOff");
            CurveDwellLabel.Text = T("CurveDwell");
            PredictiveCoolingCheckBox.Content = T("PredictiveCooling");
            UsePowerProfilesCheckBox.Content = T("PowerProfiles");
            AcPolicyLabel.Text = T("AcPolicy");
            BatteryPolicyLabel.Text = T("BatteryPolicy");
            ImportProfileButton.Content = T("ImportProfile");
            ExportProfileButton.Content = T("ExportProfile");
            HardwareSupportLabel.Text = T("HardwareSupport");
            HardwareSupportText.Text = T(_hardwareSupport.LocalizationKey);
            Fan0MeasureLabel.Text = T("Fan1") + ": ";
            Fan1MeasureLabel.Text = T("Fan2") + ": ";
            MeasureRpmButton.Content = T("MeasureRpm");
            HealthTestButton.Content = T("HealthTest");
            UpdateRpmDisplay();
            OpenTelemetryButton.Content = T("OpenTelemetry");
            StartWithWindowsCheckBox.Content = T("StartWithWindows");
            StartMinimizedCheckBox.Content = T("StartMinimized");
            AlwaysOnTopCheckBox.Content = T("AlwaysOnTop");
            CloseToTrayCheckBox.Content = T("CloseToTray");
            NotificationsCheckBox.Content = T("Notifications");
            LanguageLabel.Text = T("Language");
            StartupDelayLabel.Text = T("StartupDelay");
            ResetControlButton.Content = T("ResetControl");
            OpenLogButton.Content = T("OpenLog");
            PowerProfileHintText.Text = T("AcPolicy") + ": " + _settings.AcAggressivenessPercent + "%  •  " +
                                        T("BatteryPolicy") + ": " + _settings.BatteryAggressivenessPercent + "%";

            foreach (var entry in BoostMinutesComboBox.Items)
            {
                if (entry is ComboBoxItem item)
                {
                    item.Content = Convert.ToString(item.Tag, CultureInfo.InvariantCulture) + " min";
                }
            }
            foreach (var entry in StartupDelayComboBox.Items)
            {
                if (entry is ComboBoxItem item)
                {
                    item.Content = Convert.ToString(item.Tag, CultureInfo.InvariantCulture) + " " + T("Seconds");
                }
            }

            _loadingSettings = true;
            try
            {
                PopulateProfiles();
                PopulateLanguages();
            }
            finally
            {
                _loadingSettings = false;
            }
            ModeText.Text = _lastKnownMaximum ? T("ModeMaximum") : T("ModeAutomatic");
            UpdateAggressivenessPolicyText();
            UpdateBoostText();
            UpdateCurveControls();
            UpdateCurvePresentation(_lastTemperatureSignal);
            UpdateTrayMenu();
        }

        private void PopulateProfiles()
        {
            var selected = CoolingProfileCatalog.Match(_settings.CoolingAggressivenessPercent);
            ProfileComboBox.Items.Clear();
            foreach (var profile in CoolingProfileCatalog.BuiltIn)
            {
                ProfileComboBox.Items.Add(new Choice<CoolingProfileId>(profile.Id, T(profile.LocalizationKey)));
            }
            if (selected == CoolingProfileId.Custom)
            {
                ProfileComboBox.Items.Add(new Choice<CoolingProfileId>(CoolingProfileId.Custom, T("ProfileCustom")));
            }
            SelectChoice(ProfileComboBox, selected);
        }

        private void PopulateLanguages()
        {
            LanguageComboBox.Items.Clear();
            foreach (var language in LocalizationCatalog.SupportedLanguages)
            {
                LanguageComboBox.Items.Add(new Choice<string>(language.Code, language.NativeName));
            }
            SelectChoice(LanguageComboBox, _settings.LanguageCode);
        }

        private static void SelectChoice<TValue>(ComboBox box, TValue value)
        {
            foreach (var entry in box.Items)
            {
                if (entry is Choice<TValue> choice && EqualityComparer<TValue>.Default.Equals(choice.Value, value))
                {
                    box.SelectedItem = entry;
                    return;
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _requestInFlight = true;
            SetButtons(false);
            try
            {
                var telemetry = await Task.Run(() => _telemetry.ReadForDisplay());
                _lastRawTelemetry = telemetry;
                if (_hardwareSupport.CanControl)
                {
                    var snapshot = await Task.Run(() => _guard.ReadStatus());
                    try
                    {
                        var measured = await Task.Run(() => _guard.Measure());
                        RecordEcMeasurement(measured);
                        snapshot = measured;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Initial EC temperature measurement failed; cached/status telemetry will be used.", ex);
                    }
                    UpdateModeClock(snapshot);
                    Present(snapshot, T("Ready"), MergeTelemetry(telemetry, DateTime.UtcNow));
                }
                else
                {
                    PresentMonitoring(telemetry);
                }
            }
            catch (Exception ex)
            {
                PresentFailure(T("Connecting"), ex);
            }
            finally
            {
                _requestInFlight = false;
                if (!_closing) _timer.Start();
                SetButtons(!_closing);
                LogAttachedMeasurement();
            }
        }

        private async void TimedBoostButton_Click(object sender, RoutedEventArgs e)
        {
            await RunCommand(() => _guard.SetMaximum(), T("TimedBoost"), () =>
            {
                _boostEndsUtc = DateTime.UtcNow.AddMinutes(_settings.BoostMinutes);
                UpdateBoostText();
            });
        }

        private async void ConstantMaximumButton_Click(object sender, RoutedEventArgs e)
        {
            await RunCommand(() => _guard.SetMaximum(), T("PolicyMax"), () => SelectGeneralPolicy(100));
        }

        private async void AutomaticButton_Click(object sender, RoutedEventArgs e)
        {
            await RunCommand(() => _guard.SetAutomatic(), T("PolicyAuto"), () => SelectGeneralPolicy(0));
        }

        private void SelectGeneralPolicy(int percent)
        {
            _boostEndsUtc = null;
            _settings.UsePowerProfiles = false;
            _settings.CustomCurveEnabled = false;
            SetAggressiveness(percent);
            UpdatePowerProfileControls();
            UpdateCurveControls();
            UpdateBoostText();
            SaveSettings();
        }

        private async Task<bool> BeginOperatorRequestAsync()
        {
            if (_requestInFlight || _closing || !_hardwareSupport.CanControl) return false;
            _requestInFlight = true;
            SetButtons(false);
            var poll = _activePoll;
            if (poll != null)
            {
                if (!poll.IsCompleted)
                {
                    AppLogger.Info("Operator request accepted while a sensor poll was in progress.");
                    StatusText.Text = T("Connecting") + "…";
                    if (await Task.WhenAny(poll, Task.Delay(OperatorWaitForPoll)) != poll)
                    {
                        if (!_closing) StatusText.Text = new TimeoutException().Message;
                        AppLogger.Info("Operator request expired before the sensor poll finished; no command sent.");
                        EndOperatorRequest();
                        return false;
                    }
                }
                if (!await poll)
                {
                    EndOperatorRequest();
                    return false;
                }
            }
            if (!_closing) return true;
            EndOperatorRequest();
            return false;
        }

        private void EndOperatorRequest()
        {
            _requestInFlight = false;
            SetButtons(!_closing);
        }

        private async Task RunCommand(Func<FanGuardSnapshot> command, string success, Action applySelection)
        {
            var selectionVersion = _policySelectionVersion;
            if (!await BeginOperatorRequestAsync()) return;
            try
            {
                if (selectionVersion != _policySelectionVersion) return;
                StatusText.Text = T("Connecting") + "…";
                var snapshot = await Task.Run(command);
                _fanPolicyActive = true;
                if (_closing)
                {
                    await Task.Run(() => _guard.SetAutomatic());
                    return;
                }
                if (selectionVersion == _policySelectionVersion) applySelection();
                UpdateModeClock(snapshot);
                Present(snapshot, success, MergeLastTelemetry(DateTime.UtcNow));
                if (!_closing) _timer.Start();
            }
            catch (Exception ex)
            {
                PresentFailure(T("ModeError"), ex);
            }
            finally
            {
                EndOperatorRequest();
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (_closing) return;
            UpdateRpmDisplay();
            if (_requestInFlight || _activePoll != null) return;
            try
            {
                _activePoll = PollAsync();
                await _activePoll;
            }
            finally
            {
                _activePoll = null;
            }
        }

        private async Task<bool> PollAsync()
        {
            var applyPolicy = _fanPolicyActive;
            try
            {
                _timerTicks++;
                var telemetryTask = Task.Run(() => _telemetry.ReadForDisplay());
                if (!_hardwareSupport.CanControl)
                {
                    var monitoring = await telemetryTask;
                    PresentMonitoring(monitoring);
                    return true;
                }

                var guardTask = Task.Run(() => applyPolicy ? _guard.SendHeartbeat() : _guard.ReadStatus());
                await Task.WhenAll(guardTask, telemetryTask);
                var snapshot = guardTask.Result;
                if (_timerTicks % EcMeasurementIntervalTicks == 0)
                {
                    AutomationProperties.SetItemStatus(MeasureRpmButton, T("SensorEc"));
                    try
                    {
                        var measured = await Task.Run(() => _guard.Measure());
                        RecordEcMeasurement(measured);
                        snapshot = measured;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Periodic EC temperature measurement failed; the last valid sample will expire safely.", ex);
                    }
                    finally
                    {
                        AutomationProperties.SetItemStatus(MeasureRpmButton, string.Empty);
                    }
                }
                var now = DateTime.UtcNow;
                _lastRawTelemetry = telemetryTask.Result;
                var telemetry = MergeTelemetry(_lastRawTelemetry, now);
                if (_closing) return false;
                UpdateModeClock(snapshot);
                if (!applyPolicy)
                {
                    Present(snapshot, T("Ready"), telemetry);
                    LogAttachedMeasurement();
                    return true;
                }

                var timedBoostActive = _boostEndsUtc.HasValue && now < _boostEndsUtc.Value;
                if (_boostEndsUtc.HasValue && !timedBoostActive)
                {
                    _boostEndsUtc = null;
                    Notify(T("BoostReady"), T("PolicyAuto"));
                }

                var sampleUtc = telemetry.TemperatureTimestamp.ToUniversalTime();
                var emergency = _emergencyController.IsEmergency ? SafetyDecision.StayEmergency : SafetyDecision.None;
                if (!telemetry.TemperatureCelsius.HasValue || sampleUtc != _lastSafetySampleUtc)
                {
                    emergency = _emergencyController.Evaluate(telemetry.TemperatureCelsius);
                    _lastSafetySampleUtc = telemetry.TemperatureCelsius.HasValue ? sampleUtc : (DateTime?)null;
                }
                var emergencyActive = emergency == SafetyDecision.EnterEmergency ||
                                      emergency == SafetyDecision.StayEmergency;
                if (emergencyActive && !_emergencyNotificationShown)
                {
                    _emergencyNotificationShown = true;
                    Notify(T("ModeMaximum"), T("PolicyMax"));
                }
                else if (!emergencyActive)
                {
                    _emergencyNotificationShown = false;
                }

                var effectivePercent = EffectiveAggressiveness(telemetry);
                if (now < _profileArmUtc) effectivePercent = 0;
                var timeInCurrentMode = now >= _modeChangedUtc ? now - _modeChangedUtc : TimeSpan.Zero;
                bool shouldUseMaximum;
                if (_settings.CustomCurveEnabled && now >= _profileArmUtc)
                {
                    shouldUseMaximum = BinaryFanCurvePolicy.ShouldUseMaximum(
                        _settings.CurveTurnOnCelsius,
                        _settings.CurveTurnOffCelsius,
                        TimeSpan.FromSeconds(_settings.CurveDwellSeconds),
                        _lastTemperatureSignal.ControlTemperatureCelsius ?? telemetry.TemperatureCelsius,
                        _lastTemperatureSignal.IsStale ? 0 : _lastTemperatureSignal.TrendCelsiusPerMinute,
                        _settings.PredictiveCoolingEnabled,
                        snapshot.IsMaximum,
                        timeInCurrentMode,
                        timedBoostActive,
                        emergencyActive);
                }
                else
                {
                    shouldUseMaximum = emergencyActive || CoolingAggressivenessPolicy.ShouldUseMaximum(
                        effectivePercent, telemetry.TemperatureCelsius, snapshot.IsMaximum, timeInCurrentMode, timedBoostActive);
                }

                if (shouldUseMaximum && !snapshot.IsMaximum)
                {
                    snapshot = await Task.Run(() => _guard.SetMaximum());
                    UpdateModeClock(snapshot);
                }
                else if (!shouldUseMaximum && !snapshot.IsAutomatic)
                {
                    snapshot = await Task.Run(() => _guard.SetAutomatic());
                    UpdateModeClock(snapshot);
                }

                UpdateBoostText();
                Present(snapshot, BuildAutomationStatus(effectivePercent, timedBoostActive), telemetry);
                LogAttachedMeasurement();
                return true;
            }
            catch (Exception ex)
            {
                if (applyPolicy && _hardwareSupport.CanControl && _lastKnownMaximum)
                {
                    _boostEndsUtc = null;
                    try
                    {
                        UpdateModeClock(await Task.Run(() => _guard.SetAutomatic()));
                    }
                    catch (Exception recoveryError)
                    {
                        AppLogger.Error("AUTO recovery after telemetry failure did not complete.", recoveryError);
                    }
                }
                PresentFailure(T("ModeError"), ex);
                return false;
            }
        }

        private int EffectiveAggressiveness(TelemetrySnapshot telemetry) =>
            CoolingProfileCatalog.EffectiveAggressiveness(
                _settings.CoolingAggressivenessPercent,
                _settings.UsePowerProfiles && telemetry.OnAcPower.HasValue,
                _settings.AcAggressivenessPercent,
                _settings.BatteryAggressivenessPercent,
                telemetry.OnAcPower.GetValueOrDefault());

        private string BuildAutomationStatus(int effectivePercent, bool timedBoostActive)
        {
            if (timedBoostActive && _boostEndsUtc.HasValue)
            {
                var remaining = _boostEndsUtc.Value - DateTime.UtcNow;
                return T("TimedBoost") + " • " + string.Format(CultureInfo.CurrentCulture, "{0:00}:{1:00}",
                    Math.Max(0, (int)remaining.TotalMinutes), Math.Max(0, remaining.Seconds));
            }
            if (_settings.CustomCurveEnabled)
            {
                return string.Format(CultureInfo.CurrentCulture, T("CustomCurveStatus"),
                    _settings.CurveTurnOnCelsius, _settings.CurveTurnOffCelsius,
                    _settings.CurveDwellSeconds);
            }
            if (effectivePercent == 0) return T("PolicyAuto");
            if (effectivePercent == 100) return T("PolicyMax");
            var thresholds = CoolingAggressivenessPolicy.GetThresholds(effectivePercent);
            return string.Format(CultureInfo.CurrentCulture, T("PolicyCurve"), thresholds.TurnOnCelsius, thresholds.TurnOffCelsius);
        }

        private void Present(FanGuardSnapshot snapshot, string status, TelemetrySnapshot? liveTelemetry)
        {
            if (liveTelemetry != null)
            {
                AddTemperature(liveTelemetry.TemperatureCelsius, liveTelemetry.Timestamp);
                if (_settings.TelemetryLoggingEnabled) _history.Append(liveTelemetry, snapshot.IsMaximum ? "MAX" : "AUTO");
            }
            ModeText.Text = snapshot.IsMaximum ? T("ModeMaximum") : T("ModeAutomatic");
            ModeText.Foreground = Brushes.Black;
            var temperature = liveTelemetry?.TemperatureCelsius;
            var critical = liveTelemetry?.FirmwareCriticalCelsius;
            TemperatureText.Text = temperature.HasValue ? temperature.Value.ToString("0.0", CultureInfo.CurrentCulture) + " °C" : "-- °C";
            CriticalText.Text = " / " + T("Critical") + " " + (critical.HasValue ? critical.Value.ToString("0", CultureInfo.CurrentCulture) : "--");
            Fan0RpmText.Text = snapshot.IsMaximum ? "MAX" : "AUTO";
            Fan0DutyText.Text = " / " + (snapshot.IsMaximum ? T("TwoFansMaximum") : T("FirmwareManaged"));
            PresentPower(liveTelemetry);
            UpdateCurvePresentation(_lastTemperatureSignal);
            StatusText.Text = status;
            LastUpdateText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + " • EC " +
                                  snapshot.Mode.ToString("X2", CultureInfo.InvariantCulture) + "/" +
                                  snapshot.Shadow.ToString("X2", CultureInfo.InvariantCulture);
        }

        private void PresentMonitoring(TelemetrySnapshot telemetry)
        {
            _lastRawTelemetry = telemetry;
            AddTemperature(telemetry.TemperatureCelsius, telemetry.Timestamp);
            if (_settings.TelemetryLoggingEnabled) _history.Append(telemetry, "MONITOR");
            ModeText.Text = T("MonitoringOnly").ToUpper(CultureInfo.CurrentCulture);
            TemperatureText.Text = telemetry.TemperatureCelsius.HasValue
                ? telemetry.TemperatureCelsius.Value.ToString("0.0", CultureInfo.CurrentCulture) + " °C"
                : "-- °C";
            CriticalText.Text = " / " + T("Critical") + " " +
                                (telemetry.FirmwareCriticalCelsius?.ToString("0", CultureInfo.CurrentCulture) ?? "--");
            Fan0RpmText.Text = "--";
            Fan0DutyText.Text = " / " + T("MonitoringOnly");
            PresentPower(telemetry);
            UpdateCurvePresentation(TemperatureSignal.Empty);
            StatusText.Text = T("DeviceMonitoringOnly");
            LastUpdateText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        }

        private void PresentPower(TelemetrySnapshot? telemetry)
        {
            Fan1RpmText.Text = telemetry?.CpuLoadPercent.HasValue == true ? telemetry.CpuLoadPercent.Value + " %" : "-- %";
            var power = telemetry?.OnAcPower is bool ac ? T(ac ? "OnAc" : "OnBattery") : "--";
            if (telemetry?.BatteryPercent.HasValue == true) power += " " + telemetry.BatteryPercent.Value + "%";
            Fan1DutyText.Text = " / " + power;
        }

        private void RecordEcMeasurement(FanGuardSnapshot measured)
        {
            var now = DateTime.UtcNow;
            _temperatureSignalTracker.Add(measured.TemperatureCelsius, measured.ReceivedAtUtc);
            if (measured.TemperatureCelsius >= -20 && measured.TemperatureCelsius <= 120)
                _ecTemperatureTimeUtc = measured.ReceivedAtUtc;
            _lastEcCriticalCelsius = measured.CriticalCelsius;
            _lastTemperatureSignal = _temperatureSignalTracker.Read(now, EcSampleStaleAfter);
            RecordRpmMeasurement(measured);
        }

        private void RecordRpmMeasurement(FanGuardSnapshot measured)
        {
            _lastRpmMeasurement = measured;
            _peakFan0Rpm = Math.Max(_peakFan0Rpm ?? measured.Fan0Rpm, measured.Fan0Rpm);
            _peakFan1Rpm = Math.Max(_peakFan1Rpm ?? measured.Fan1Rpm, measured.Fan1Rpm);
            UpdateRpmDisplay();
        }

        private void UpdateRpmDisplay()
        {
            var sample = _lastRpmMeasurement;
            var current = sample != null && sample.Age < EcSampleStaleAfter ? sample : null;
            var culture = CultureInfo.CurrentCulture;
            VerifiedPeakRpmText.Text = (_peakFan0Rpm?.ToString("N0", culture) ?? "--") + " / " +
                                       (_peakFan1Rpm?.ToString("N0", culture) ?? "--");
            MeasuredFan0Text.Text = (current?.Fan0Rpm.ToString("N0", culture) ?? "--") + " RPM";
            MeasuredFan1Text.Text = (current?.Fan1Rpm.ToString("N0", culture) ?? "--") + " RPM";
            if (sample == null)
            {
                LastMeasurementText.Text = T("RpmDataStale");
                return;
            }
            var time = sample.ReceivedAtUtc.ToLocalTime().ToString("HH:mm:ss", culture);
            var last = string.Format(culture, T("LastRpmMeasurement"), time);
            LastMeasurementText.Text = current == null
                ? T("RpmDataStale") + " • " + last
                : last + " • " + T("SensorEc") + " " + current.Fan0Duty + "/" + current.Fan1Duty;
        }

        private void LogAttachedMeasurement()
        {
            var sample = _lastRpmMeasurement;
            if (_closing || sample == null || sample.Age >= EcSampleStaleAfter || sample == _lastLoggedMeasurement ||
                !MeasureRpmButton.IsEnabled)
                return;
            if (!_logResumedProfileMeasurements && (_fanPolicyActive || !StatusTab.IsSelected || !LastMeasurementText.IsVisible ||
                !MeasuredFan0Text.IsVisible || !MeasuredFan1Text.IsVisible))
                return;
            if (_loggedAttachmentMeasurements >= 2 && (!_logResumedProfileMeasurements ||
                _lastLoggedMeasurement?.Age < TimeSpan.FromMinutes(1)))
                return;

            _lastLoggedMeasurement = sample;
            _loggedAttachmentMeasurements++;
            AppLogger.Info($"Attached MEASURE UTC={sample.ReceivedAtUtc:O} mode={sample.Mode:X2}/{sample.Shadow:X2} " +
                $"statusSelected={StatusTab.IsSelected} hardware='{HardwareSupportText.Text}' " +
                $"rpm='{MeasuredFan0Text.Text}'/'{MeasuredFan1Text.Text}' stamp='{LastMeasurementText.Text}' " +
                $"visible(time/fan1/fan2)={LastMeasurementText.IsVisible}/{MeasuredFan0Text.IsVisible}/{MeasuredFan1Text.IsVisible} " +
                $"enabled(measure/test/maximum/automatic/boost)={MeasureRpmButton.IsEnabled}/{HealthTestButton.IsEnabled}/" +
                $"{ConstantMaximumButton.IsEnabled}/{AutomaticButton.IsEnabled}/{TimedBoostButton.IsEnabled} " +
                $"resumedProfile={_logResumedProfileMeasurements}");
        }

        private TelemetrySnapshot MergeTelemetry(TelemetrySnapshot telemetry, DateTime nowUtc)
        {
            _lastTemperatureSignal = _temperatureSignalTracker.Read(nowUtc, EcSampleStaleAfter);
            var rawAge = nowUtc - telemetry.Timestamp.ToUniversalTime();
            var rawCurrent = rawAge >= TimeSpan.Zero && rawAge < EcSampleStaleAfter;
            var thermalAge = nowUtc - telemetry.TemperatureTimestamp.ToUniversalTime();
            var thermalCurrent = thermalAge >= TimeSpan.Zero && thermalAge < EcSampleStaleAfter;
            var temperature = _lastTemperatureSignal.ControlTemperatureCelsius ?? (thermalCurrent ? telemetry.TemperatureCelsius : null);
            var critical = (_lastTemperatureSignal.IsStale ? null : _lastEcCriticalCelsius) ?? (thermalCurrent ? telemetry.FirmwareCriticalCelsius : null);
            return new TelemetrySnapshot(
                temperature,
                critical,
                rawCurrent ? telemetry.CpuLoadPercent : null,
                rawCurrent ? telemetry.OnAcPower : null,
                rawCurrent ? telemetry.BatteryPercent : null,
                telemetry.Timestamp,
                _lastTemperatureSignal.ControlTemperatureCelsius.HasValue && _ecTemperatureTimeUtc.HasValue
                    ? _ecTemperatureTimeUtc.Value : telemetry.TemperatureTimestamp);
        }

        private TelemetrySnapshot? MergeLastTelemetry(DateTime nowUtc)
        {
            return _lastRawTelemetry == null ? null : MergeTelemetry(_lastRawTelemetry, nowUtc);
        }

        private void UpdateCurvePresentation(TemperatureSignal signal)
        {
            CurveTurnOnValueText.Text = _settings.CurveTurnOnCelsius + " °C";
            CurveTurnOffValueText.Text = _settings.CurveTurnOffCelsius + " °C";
            var width = CurvePreviewCanvas.ActualWidth > 20 ? CurvePreviewCanvas.ActualWidth : 440.0;
            var onX = TemperatureToCurveX(_settings.CurveTurnOnCelsius, width);
            CurvePreviewLine.Points = new PointCollection
            {
                new Point(0, 35), new Point(onX, 35), new Point(onX, 11), new Point(width, 11)
            };

            CurveReturnThreshold.X1 = CurveReturnThreshold.X2 = TemperatureToCurveX(_settings.CurveTurnOffCelsius, width);
            CurveReturnThreshold.ToolTip = "AUTO • " + CurveTurnOffValueText.Text;
            CurvePreviewLine.ToolTip = "MAX • " + CurveTurnOnValueText.Text;
            var raw = _lastRawTelemetry;
            var rawAge = raw == null ? TimeSpan.MaxValue : DateTime.UtcNow - raw.TemperatureTimestamp.ToUniversalTime();
            var ec = signal.ControlTemperatureCelsius;
            var current = ec ?? (rawAge >= TimeSpan.Zero && rawAge < EcSampleStaleAfter ? raw?.TemperatureCelsius : null);
            var sampleTime = ec.HasValue ? _ecTemperatureTimeUtc : raw?.TemperatureTimestamp;
            TemperatureText.ToolTip = current.HasValue && sampleTime.HasValue
                ? T(ec.HasValue ? "SensorEc" : "SensorAcpi") + " • " + sampleTime.Value.ToLocalTime().ToString("HH:mm:ss")
                : T("SensorUnavailable");
            if (!current.HasValue)
            {
                CurveCurrentMarker.Visibility = Visibility.Collapsed;
                CurveSensorText.Text = T("SensorUnavailable");
            }
            else
            {
                var currentX = TemperatureToCurveX(current.Value, width);
                CurveCurrentMarker.X1 = currentX;
                CurveCurrentMarker.X2 = currentX;
                CurveCurrentMarker.Visibility = Visibility.Visible;
                var age = signal.Age == TimeSpan.MaxValue ? "--" : Math.Max(0, (int)Math.Round(signal.Age.TotalSeconds)).ToString(CultureInfo.CurrentCulture);
                CurveSensorText.Text = ec.HasValue
                    ? string.Format(CultureInfo.CurrentCulture, T("SensorEcLive"), current.Value, age, signal.TrendCelsiusPerMinute)
                    : T("SensorAcpi") + " • " + current.Value.ToString("0.0", CultureInfo.CurrentCulture) + " °C";
            }

            CurveHintText.Text = signal.MinimumCelsius.HasValue && signal.MaximumCelsius.HasValue
                ? string.Format(CultureInfo.CurrentCulture, T("SessionTemperatureRange"), signal.MinimumCelsius.Value, signal.MaximumCelsius.Value)
                : T("CurveAutoMaxHint");
        }

        private static double TemperatureToCurveX(double temperatureCelsius, double width)
        {
            var normalized = Math.Max(0.0, Math.Min(1.0, (temperatureCelsius - 40.0) / 50.0));
            return normalized * width;
        }

        private void PresentFailure(string prefix, Exception exception, bool modeUnknown = true)
        {
            if (modeUnknown)
            {
                ModeText.Text = T("ModeError");
                ModeText.Foreground = Brushes.Black;
            }
            StatusText.Text = prefix + ": " + exception.Message;
            AppLogger.Error(prefix, exception);
            Notify(T("ModeError"), exception.Message);
        }

        private void AddTemperature(double? temperature, DateTime timestamp)
        {
            var time = timestamp.ToUniversalTime();
            if (_temperatureHistory.Count > 0 && time <= _temperatureHistory[_temperatureHistory.Count - 1].TimeUtc)
            {
                if (time == _temperatureHistory[_temperatureHistory.Count - 1].TimeUtc) return;
                _temperatureHistory.Clear();
            }
            _temperatureHistory.RemoveAll(sample => (time - sample.TimeUtc).TotalSeconds > 60);
            _temperatureHistory.Add((time, temperature));
            DrawTemperatureGraph();
        }

        private void TemperatureGraph_Changed(object sender, SizeChangedEventArgs e) => DrawTemperatureGraph();
        private void TemperatureGraph_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => DrawTemperatureGraph();

        private void DrawTemperatureGraph()
        {
            if (TemperatureGraphCanvas?.IsVisible != true || TemperatureGraphLine == null) return;
            var end = DateTime.UtcNow;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                DateTime? previous = null;
                foreach (var sample in _temperatureHistory)
                {
                    var age = (end - sample.TimeUtc).TotalSeconds;
                    if (age < 0 || age > 60 || !sample.Celsius.HasValue) { previous = null; continue; }
                    var fraction = Math.Max(0, Math.Min(1, (sample.Celsius.Value - 35) / 60));
                    var point = new Point((1 - age / 60) * TemperatureGraphCanvas.ActualWidth,
                        (1 - fraction) * TemperatureGraphCanvas.ActualHeight);
                    if (!previous.HasValue || (sample.TimeUtc - previous.Value).TotalSeconds > 3)
                        context.BeginFigure(point, false, false);
                    else
                        context.LineTo(point, true, false);
                    previous = sample.TimeUtc;
                }
            }
            geometry.Freeze();
            TemperatureGraphLine.Data = geometry;
        }

        private void UpdateModeClock(FanGuardSnapshot snapshot)
        {
            if (snapshot.IsMaximum != _lastKnownMaximum)
            {
                _lastKnownMaximum = snapshot.IsMaximum;
                _modeChangedUtc = DateTime.UtcNow;
            }
        }

        private void SetButtons(bool enabled)
        {
            var control = enabled && _hardwareSupport.CanControl;
            TimedBoostButton.IsEnabled = control;
            ConstantMaximumButton.IsEnabled = control;
            AutomaticButton.IsEnabled = control;
            MeasureRpmButton.IsEnabled = control;
            HealthTestButton.IsEnabled = control;
            ResetControlButton.IsEnabled = enabled;
        }

        private void MarkPolicySelectionChanged()
        {
            _policySelectionVersion++;
            _fanPolicyActive = true;
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || !(ProfileComboBox.SelectedItem is Choice<CoolingProfileId> choice) ||
                choice.Value == CoolingProfileId.Custom) return;
            MarkPolicySelectionChanged();
            SelectGeneralPolicy(CoolingProfileCatalog.Get(choice.Value).AggressivenessPercent);
        }

        private void AggressivenessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingSettings) return;
            MarkPolicySelectionChanged();
            SelectGeneralPolicy((int)Math.Round(e.NewValue));
        }

        private void AggressivenessTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ApplyAggressivenessText();

        private void AggressivenessTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            ApplyAggressivenessText();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void ApplyAggressivenessText()
        {
            if (!int.TryParse(AggressivenessTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ||
                Math.Max(0, Math.Min(100, value)) == _settings.CoolingAggressivenessPercent)
            {
                AggressivenessTextBox.Text = _settings.CoolingAggressivenessPercent.ToString(CultureInfo.CurrentCulture);
                return;
            }
            MarkPolicySelectionChanged();
            SelectGeneralPolicy(value);
        }

        private void SetAggressiveness(int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            _loadingSettings = true;
            try
            {
                _settings.CoolingAggressivenessPercent = value;
                AggressivenessSlider.Value = value;
                AggressivenessTextBox.Text = value.ToString(CultureInfo.CurrentCulture);
                PopulateProfiles();
                UpdateAggressivenessPolicyText();
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        private void UpdateAggressivenessPolicyText()
        {
            var percent = _settings.CoolingAggressivenessPercent;
            if (percent == 0) AggressivenessPolicyText.Text = T("PolicyAuto");
            else if (percent == 100) AggressivenessPolicyText.Text = T("PolicyMax");
            else
            {
                var thresholds = CoolingAggressivenessPolicy.GetThresholds(percent);
                AggressivenessPolicyText.Text = string.Format(CultureInfo.CurrentCulture, T("PolicyCurve"),
                    thresholds.TurnOnCelsius, thresholds.TurnOffCelsius);
            }
        }

        private void PowerProfilesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            MarkPolicySelectionChanged();
            _settings.UsePowerProfiles = UsePowerProfilesCheckBox.IsChecked == true;
            if (_settings.UsePowerProfiles) _settings.CustomCurveEnabled = false;
            UpdatePowerProfileControls();
            UpdateCurveControls();
            SaveSettings();
        }

        private void PowerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingSettings) return;
            MarkPolicySelectionChanged();
            _settings.AcAggressivenessPercent = (int)Math.Round(AcAggressivenessSlider.Value);
            _settings.BatteryAggressivenessPercent = (int)Math.Round(BatteryAggressivenessSlider.Value);
            UpdatePowerProfileControls();
            SaveSettings();
        }

        private void UpdatePowerProfileControls()
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                UsePowerProfilesCheckBox.IsChecked = _settings.UsePowerProfiles;
                AcAggressivenessSlider.IsEnabled = _settings.UsePowerProfiles;
                BatteryAggressivenessSlider.IsEnabled = _settings.UsePowerProfiles;
                AcAggressivenessSlider.Value = _settings.AcAggressivenessPercent;
                BatteryAggressivenessSlider.Value = _settings.BatteryAggressivenessPercent;
                AcAggressivenessText.Text = _settings.AcAggressivenessPercent + "%";
                BatteryAggressivenessText.Text = _settings.BatteryAggressivenessPercent + "%";
                PowerProfileHintText.Text = T("AcPolicy") + ": " + _settings.AcAggressivenessPercent + "%  •  " +
                                            T("BatteryPolicy") + ": " + _settings.BatteryAggressivenessPercent + "%";
            }
            finally
            {
                _loadingSettings = wasLoading;
            }
        }

        private void CustomCurveSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            MarkPolicySelectionChanged();
            _settings.CustomCurveEnabled = CustomCurveEnabledCheckBox.IsChecked == true;
            _settings.PredictiveCoolingEnabled = PredictiveCoolingCheckBox.IsChecked == true;
            if (_settings.CustomCurveEnabled)
            {
                _settings.UsePowerProfiles = false;
                UpdatePowerProfileControls();
            }
            UpdateCurveControls();
            SaveSettings();
        }

        private void CurveSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingSettings) return;
            MarkPolicySelectionChanged();
            var turnOn = (int)Math.Round(CurveTurnOnSlider.Value);
            var turnOff = (int)Math.Round(CurveTurnOffSlider.Value);
            if (turnOff > turnOn - 3)
            {
                turnOff = turnOn - 3;
            }
            _settings.CurveTurnOnCelsius = turnOn;
            _settings.CurveTurnOffCelsius = Math.Max(40, turnOff);
            _loadingSettings = true;
            try
            {
                CurveTurnOffSlider.Maximum = Math.Max(40, turnOn - 3);
                CurveTurnOffSlider.Value = _settings.CurveTurnOffCelsius;
            }
            finally
            {
                _loadingSettings = false;
            }
            UpdateCurvePresentation(_lastTemperatureSignal);
            SaveSettings();
        }

        private void CurveDwellComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || !(CurveDwellComboBox.SelectedItem is ComboBoxItem item)) return;
            MarkPolicySelectionChanged();
            _settings.CurveDwellSeconds = Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture);
            SaveSettings();
            UpdateCurvePresentation(_lastTemperatureSignal);
        }

        private void SelectCurveDwell(int seconds)
        {
            foreach (var entry in CurveDwellComboBox.Items)
            {
                if (entry is ComboBoxItem item && Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == seconds)
                {
                    CurveDwellComboBox.SelectedItem = item;
                    return;
                }
            }
            CurveDwellComboBox.SelectedIndex = 1;
        }

        private void UpdateCurveControls()
        {
            var wasLoading = _loadingSettings;
            _loadingSettings = true;
            try
            {
                CustomCurveEnabledCheckBox.IsChecked = _settings.CustomCurveEnabled;
                PredictiveCoolingCheckBox.IsChecked = _settings.PredictiveCoolingEnabled;
                CurveTurnOnSlider.Value = _settings.CurveTurnOnCelsius;
                CurveTurnOffSlider.Maximum = Math.Max(40, _settings.CurveTurnOnCelsius - 3);
                CurveTurnOffSlider.Value = _settings.CurveTurnOffCelsius;
                SelectCurveDwell(_settings.CurveDwellSeconds);
                var enabled = _hardwareSupport.CanControl && _settings.CustomCurveEnabled;
                CurveTurnOnSlider.IsEnabled = enabled;
                CurveTurnOffSlider.IsEnabled = enabled;
                CurveDwellComboBox.IsEnabled = enabled;
                PredictiveCoolingCheckBox.IsEnabled = enabled;
            }
            finally
            {
                _loadingSettings = wasLoading;
            }
            UpdateCurvePresentation(_lastTemperatureSignal);
        }

        private void BoostMinutesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || !(BoostMinutesComboBox.SelectedItem is ComboBoxItem item) ||
                !int.TryParse(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var minutes)) return;
            _settings.BoostMinutes = minutes;
            SaveSettings();
            UpdateBoostText();
        }

        private void SelectBoostMinutes(int minutes)
        {
            foreach (var entry in BoostMinutesComboBox.Items)
            {
                if (entry is ComboBoxItem item && Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == minutes)
                {
                    BoostMinutesComboBox.SelectedItem = item;
                    return;
                }
            }
            BoostMinutesComboBox.SelectedIndex = 1;
        }

        private void UpdateBoostText()
        {
            if (!_boostEndsUtc.HasValue)
            {
                BoostCountdownText.Text = T("BoostReady");
                return;
            }
            var remaining = _boostEndsUtc.Value - DateTime.UtcNow;
            BoostCountdownText.Text = remaining <= TimeSpan.Zero ? T("BoostReady") :
                string.Format(CultureInfo.CurrentCulture, "{0:00}:{1:00}", Math.Max(0, (int)remaining.TotalMinutes), Math.Max(0, remaining.Seconds));
        }

        private async void MeasureRpmButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await BeginOperatorRequestAsync()) return;
            try
            {
                var measured = await Task.Run(() => _guard.Measure());
                PresentMeasurement(measured, T("MeasureRpm"));
            }
            catch (Exception ex)
            {
                PresentFailure(T("MeasureRpm"), ex, modeUnknown: false);
            }
            finally
            {
                EndOperatorRequest();
            }
        }

        private async void HealthTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await BeginOperatorRequestAsync()) return;
            StatusText.Text = T("HealthTesting");
            var restoreMaximum = _lastKnownMaximum;
            try
            {
                var baseline = await Task.Run(() => _guard.Measure());
                if (_closing) return;
                RecordRpmMeasurement(baseline);
                restoreMaximum = baseline.IsMaximum;
                await Task.Run(() => _guard.SetMaximum());
                if (restoreMaximum) _fanPolicyActive = true;
                for (var sample = 0; sample < 6; sample++)
                {
                    await Task.Delay(750);
                    if (_closing) return;
                    await Task.Run(() => _guard.SendHeartbeat());
                }
                var peak = await Task.Run(() => _guard.Measure());
                PresentMeasurement(peak, T("HealthTesting"));
                var reading = new EcFanReading(peak.Fan0Rpm, (byte)peak.Fan0Duty, peak.Fan1Rpm, (byte)peak.Fan1Duty);
                string result;
                if (FanHealthAssessment.IsStrongCooling(reading) ||
                    (peak.Fan0Rpm >= baseline.Fan0Rpm + 1500 && peak.Fan1Rpm >= baseline.Fan1Rpm + 1500))
                    result = T("HealthPassed");
                else if (peak.Fan0Rpm == 0 || peak.Fan1Rpm == 0)
                    result = T("HealthFailed");
                else
                    result = T("HealthInconclusive");
                StatusText.Text = result;
                Notify(T("HealthTest"), result);
            }
            catch (Exception ex)
            {
                PresentFailure(T("HealthTest"), ex);
            }
            finally
            {
                try
                {
                    // A test that began in MAX already requested MAX. Respect any subsequent firmware recovery.
                    var keepMaximum = restoreMaximum && !_closing;
                    var restored = await Task.Run(() => keepMaximum ? _guard.ReadStatus() : _guard.SetAutomatic());
                    UpdateModeClock(restored);
                    Present(restored, StatusText.Text, MergeLastTelemetry(DateTime.UtcNow));
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Fan test state restoration failed; service failsafe remains active.", ex);
                }
                EndOperatorRequest();
            }
        }

        private void PresentMeasurement(FanGuardSnapshot measured, string prefix)
        {
            RecordEcMeasurement(measured);
            UpdateModeClock(measured);
            Present(measured, prefix + " • " + LastMeasurementText.Text, MergeLastTelemetry(DateTime.UtcNow));
        }

        private void ImportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Fan profile (*.json)|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var imported = _settings.Copy();
                ProfileExchange.Import(dialog.FileName, imported);
                _settingsStore.Save(imported);
                _settings = imported;
                MarkPolicySelectionChanged();
                ApplySettingsToControls();
                ApplyLocalization();
                StatusText.Text = T("ImportProfile") + ": " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                PresentFailure(T("ImportProfile"), ex, modeUnknown: false);
            }
        }

        private void ExportProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Fan profile (*.json)|*.json", FileName = "fan-profile.json", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                ProfileExchange.Export(dialog.FileName, _settings);
                StatusText.Text = T("ExportProfile") + ": " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                PresentFailure(T("ExportProfile"), ex, modeUnknown: false);
            }
        }

        private async void ResetControlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_hardwareSupport.CanControl)
                await RunCommand(() => _guard.SetAutomatic(), T("PolicyAuto"), () => SelectGeneralPolicy(0));
            else
                SelectGeneralPolicy(0);
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || !(LanguageComboBox.SelectedItem is Choice<string> language)) return;
            _settings.LanguageCode = language.Value;
            ApplyLocalization();
            SaveSettings();
        }

        private void StartupDelayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingSettings || !(StartupDelayComboBox.SelectedItem is ComboBoxItem item)) return;
            _settings.StartupDelaySeconds = Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture);
            SaveSettings();
        }

        private void SelectStartupDelay(int seconds)
        {
            foreach (var entry in StartupDelayComboBox.Items)
            {
                if (entry is ComboBoxItem item && Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == seconds)
                {
                    StartupDelayComboBox.SelectedItem = item;
                    return;
                }
            }
            StartupDelayComboBox.SelectedIndex = 1;
        }

        private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            var requested = StartWithWindowsCheckBox.IsChecked == true;
            try
            {
                StartupRegistration.SetEnabled(requested);
                if (StartupRegistration.IsEnabled() != requested) throw new InvalidOperationException("Startup registration verification failed.");
                StatusText.Text = T("StartWithWindows") + ": " + (requested ? "ON" : "OFF");
            }
            catch (Exception ex)
            {
                _loadingSettings = true;
                StartWithWindowsCheckBox.IsChecked = !requested;
                _loadingSettings = false;
                PresentFailure(T("StartWithWindows"), ex, modeUnknown: false);
            }
        }

        private void UserSettingCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettings) return;
            _settings.StartMinimized = StartMinimizedCheckBox.IsChecked == true;
            _settings.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
            _settings.CloseToTray = CloseToTrayCheckBox.IsChecked == true;
            _settings.NotificationsEnabled = NotificationsCheckBox.IsChecked == true;
            Topmost = _settings.AlwaysOnTop;
            SaveSettings();
        }

        private void SaveSettings()
        {
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Error("User settings could not be saved.", ex);
                StatusText.Text = ex.Message;
            }
        }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e) => OpenTextFile(
            File.Exists(FanGuardServiceManager.GuardLogPath) ? FanGuardServiceManager.GuardLogPath : AppLogger.LogPath,
            T("OpenLog"));

        private void OpenTelemetryButton_Click(object sender, RoutedEventArgs e) => OpenTextFile(TelemetryHistoryStore.CsvPath, T("OpenTelemetry"));

        private void OpenTextFile(string path, string title)
        {
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException(title, path);
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void InitializeTrayIcon()
        {
            var executable = Process.GetCurrentProcess().MainModule?.FileName;
            _trayDrawingIcon = string.IsNullOrWhiteSpace(executable)
                ? (Drawing.Icon)Drawing.SystemIcons.Application.Clone()
                : Drawing.Icon.ExtractAssociatedIcon(executable) ?? (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
            _trayIcon = new Forms.NotifyIcon
            {
                Icon = _trayDrawingIcon,
                Visible = true,
                Text = TruncateTrayText(T("AppTitle"))
            };
            _trayIcon.DoubleClick += (_, __) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
            UpdateTrayMenu();
        }

        private void UpdateTrayMenu()
        {
            if (_trayIcon == null) return;
            _trayIcon.Text = TruncateTrayText(T("AppTitle"));
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(T("Show"), null, (_, __) => Dispatcher.BeginInvoke(new Action(ShowFromTray)));
            if (_hardwareSupport.CanControl)
            {
                menu.Items.Add(T("Automatic"), null, (_, __) => Dispatcher.BeginInvoke(new Action(() => AutomaticButton_Click(this, new RoutedEventArgs()))));
                menu.Items.Add(T("ConstantMaximum"), null, (_, __) => Dispatcher.BeginInvoke(new Action(() => ConstantMaximumButton_Click(this, new RoutedEventArgs()))));
            }
            menu.Items.Add(T("Exit"), null, (_, __) => Dispatcher.BeginInvoke(new Action(ExitFromTray)));
            var old = _trayIcon.ContextMenuStrip;
            _trayIcon.ContextMenuStrip = menu;
            old?.Dispose();
        }

        private static string TruncateTrayText(string value) => value.Length <= 63 ? value : value.Substring(0, 63);

        internal void ShowFromTray()
        {
            if (_closing) return;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitFromTray()
        {
            _allowClose = true;
            Close();
        }

        private void Notify(string title, string message)
        {
            if (!_settings.NotificationsEnabled || _trayIcon == null) return;
            _trayIcon.ShowBalloonTip(4000, TruncateTrayText(title), message, Forms.ToolTipIcon.Info);
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            if (!_hardwareSupport.CanControl) return;
            _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _windowSource?.AddHook(WindowMessageHook);
            var handle = new WindowInteropHelper(this).Handle;
            RegisterProfileHotKey(handle, HotKeyAutomatic, Key.D1);
            RegisterProfileHotKey(handle, HotKeyQuiet, Key.D2);
            RegisterProfileHotKey(handle, HotKeyBalanced, Key.D3);
            RegisterProfileHotKey(handle, HotKeyMaximum, Key.D4);
        }

        private static void RegisterProfileHotKey(IntPtr handle, int id, Key key)
        {
            if (!RegisterHotKey(handle, id, ModControl | ModAlt, (uint)KeyInterop.VirtualKeyFromKey(key)))
                AppLogger.Info("Could not register fan hotkey Ctrl+Alt+" + key);
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WmHotKey) return IntPtr.Zero;
            handled = true;
            switch (wParam.ToInt32())
            {
                case HotKeyAutomatic:
                    AutomaticButton_Click(this, new RoutedEventArgs());
                    break;
                case HotKeyQuiet:
                    ApplyHotKeyProfile(CoolingProfileId.Quiet);
                    break;
                case HotKeyBalanced:
                    ApplyHotKeyProfile(CoolingProfileId.Balanced);
                    break;
                case HotKeyMaximum:
                    ConstantMaximumButton_Click(this, new RoutedEventArgs());
                    break;
            }
            return IntPtr.Zero;
        }

        private void ApplyHotKeyProfile(CoolingProfileId id)
        {
            MarkPolicySelectionChanged();
            SelectGeneralPolicy(CoolingProfileCatalog.Get(id).AggressivenessPercent);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose && !_closing && _settings.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                Notify(T("AppTitle"), T("CloseToTray"));
                return;
            }

            if (!_closing)
            {
                _closing = true;
                _timer.Stop();
                _boostEndsUtc = null;
                if (_hardwareSupport.CanControl && _fanPolicyActive)
                {
                    try
                    {
                        _guard.SetAutomatic();
                        AppLogger.Info("Firmware automatic fan mode verified during exit.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Exit AUTO request did not complete; service timeout recovery remains active.", ex);
                    }
                }
                var handle = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(handle, HotKeyAutomatic);
                UnregisterHotKey(handle, HotKeyQuiet);
                UnregisterHotKey(handle, HotKeyBalanced);
                UnregisterHotKey(handle, HotKeyMaximum);
                _windowSource?.RemoveHook(WindowMessageHook);
                _trayIcon?.Dispose();
                _trayIcon = null;
                _trayDrawingIcon?.Dispose();
                _trayDrawingIcon = null;
            }
            base.OnClosing(e);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        private sealed class Choice<TValue>
        {
            public Choice(TValue value, string label)
            {
                Value = value;
                Label = label;
            }

            public TValue Value { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }
    }
}
