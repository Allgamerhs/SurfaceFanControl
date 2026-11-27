using System;
using System.Collections.Generic;
using System.Linq;

namespace SurfaceProFanControl.Core
{
    public readonly struct SupportedLanguage
    {
        public SupportedLanguage(string code, string nativeName, bool rightToLeft, string fontFamily)
        {
            Code = code;
            NativeName = nativeName;
            RightToLeft = rightToLeft;
            FontFamily = fontFamily;
        }

        public string Code { get; }
        public string NativeName { get; }
        public bool RightToLeft { get; }
        public string FontFamily { get; }
    }

    public static class LocalizationCatalog
    {
        private static readonly SupportedLanguage[] LanguageList =
        {
            new SupportedLanguage("tr", "Türkçe", false, "Tahoma"),
            new SupportedLanguage("en", "English", false, "Tahoma"),
            new SupportedLanguage("de", "Deutsch", false, "Tahoma"),
            new SupportedLanguage("it", "Italiano", false, "Tahoma"),
            new SupportedLanguage("es", "Español", false, "Tahoma"),
            new SupportedLanguage("ar", "العربية", true, "Segoe UI"),
            new SupportedLanguage("zh-Hans", "简体中文", false, "Microsoft YaHei UI"),
            new SupportedLanguage("ja", "日本語", false, "Meiryo UI"),
            new SupportedLanguage("ru", "Русский", false, "Tahoma"),
            new SupportedLanguage("pl", "Polski", false, "Tahoma")
        };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["tr"] = D(
                    "LastRpmMeasurement", "Son devir ölçümü: {0}", "RpmDataStale", "Güncel devir verisi yok",
                    "AppTitle", "Fan Kontrol Merkezi", "Header", "FAN KONTROL MERKEZİ",
                    "TabControl", "Kontrol", "TabProfiles", "Profiller", "TabStatus", "Durum", "TabSettings", "Ayarlar",
                    "Temperature", "Sıcaklık", "Critical", "kritik", "FanCommand", "Fan komutu", "CpuPower", "CPU / Güç",
                    "Profile", "Profil", "Aggressiveness", "Soğutma agresifliği", "PolicyAuto", "Firmware otomatik fan yönetimi.",
                    "PolicyMax", "Doğrulanmış sürekli maksimum fan.", "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; 5 °C histerezis, 60 sn bekleme.",
                    "VerifiedPeakRpm", "Doğrulanmış tepe RPM", "BoostReady", "Boost hazır", "TimedBoost", "Süreli MAX",
                    "ConstantMaximum", "SABİT MAX", "Automatic", "OTOMATİK", "PowerProfiles", "Prize ve bataryaya göre ayrı profil",
                    "AcPolicy", "Prizde", "BatteryPolicy", "Bataryada", "ImportProfile", "İçe aktar", "ExportProfile", "Dışa aktar",
                    "HardwareSupport", "Donanım desteği", "FullControl", "Tam kontrol", "MonitoringOnly", "Yalnız izleme",
                    "Fan1", "Fan 1", "Fan2", "Fan 2", "MeasureRpm", "RPM ölç", "HealthTest", "Fan testi", "NoMeasurement", "Henüz ölçüm yok",
                    "StartWithWindows", "Windows ile başlat", "StartMinimized", "Başlangıçta küçült", "AlwaysOnTop", "Her zaman üstte",
                    "CloseToTray", "Kapatınca bildirim alanına küçült", "Notifications", "Bildirimleri göster", "Language", "Dil",
                    "OpenLog", "Günlük", "ResetControl", "Kontrolü sıfırla", "TelemetryLogging", "Telemetri kaydı", "OpenTelemetry", "CSV aç",
                    "SettingsStored", "Ayarlar bu kullanıcı için saklanır.", "Connecting", "BAĞLANIYOR", "Ready", "HAZIR",
                    "ModeMaximum", "MAKSİMUM", "ModeAutomatic", "OTOMATİK", "ModeError", "HATA", "TwoFansMaximum", "iki fan MAX",
                    "FirmwareManaged", "firmware", "OnAc", "Prizde", "OnBattery", "Pilde", "ProfileAutomatic", "Firmware AUTO",
                    "ProfileQuiet", "Sessiz", "ProfileBalanced", "Dengeli", "ProfileCool", "Serin", "ProfileConstantMaximum", "Sabit MAX",
                    "ProfileCustom", "Özel", "Exit", "Çıkış", "Show", "Göster", "HealthTesting", "Fan yanıtı sınanıyor…",
                    "HealthPassed", "İki fan da komuta yanıt verdi.", "HealthFailed", "Fan yanıt testi başarısız.",
                    "HealthInconclusive", "Fan testi kesin sonuç vermedi.", "DeviceSupportedSurface", "Surface Pro 1 / IT8519: tam kontrol",
                    "DeviceMonitoringOnly", "Genel Windows bilgisayarı: yalnız sensör izleme", "StartupDelay", "Başlangıç gecikmesi", "Seconds", "sn",
                    "TabCurve", "Eğri", "CurveEnabled", "Özel AUTO / MAX eğrisini etkinleştir", "CurveTurnOn", "MAX eşiği", "CurveTurnOff", "Sessize dön",
                    "CurveDwell", "En az bekleme", "PredictiveCooling", "Hızlı sıcaklık artışında erken soğut", "CustomCurveStatus", "Özel eğri: MAX {0} °C / AUTO {1} °C • {2} sn",
                    "SensorAcpi", "ACPI sensörü", "SensorUnavailable", "Sıcaklık sensörü kullanılamıyor", "SensorEc", "EC sensörü", "SensorWaiting", "EC sıcaklığı bekleniyor…",
                    "SensorEcStale", "EC eski: {0:0.0} °C • {1} sn • eğim {2:+0.0;-0.0;0.0} °C/dk", "SensorEcLive", "EC canlı: {0:0.0} °C • {1} sn • eğim {2:+0.0;-0.0;0.0} °C/dk",
                    "SessionTemperatureRange", "Oturum min/maks: {0:0.0} / {1:0.0} °C", "CurveAutoMaxHint", "Düşükte firmware AUTO (sessiz), yüksekte iki fan MAX."),

                ["en"] = D(
                    "LastRpmMeasurement", "Last RPM reading: {0}", "RpmDataStale", "No current RPM data",
                    "AppTitle", "Fan Control Center", "Header", "FAN CONTROL CENTER", "TabControl", "Control", "TabProfiles", "Profiles", "TabStatus", "Status", "TabSettings", "Settings",
                    "Temperature", "Temperature", "Critical", "critical", "FanCommand", "Fan command", "CpuPower", "CPU / Power", "Profile", "Profile",
                    "Aggressiveness", "Cooling aggressiveness", "PolicyAuto", "Firmware automatic fan management.", "PolicyMax", "Verified continuous maximum fan.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; 5 °C hysteresis, 60 s dwell.", "VerifiedPeakRpm", "Verified peak RPM",
                    "BoostReady", "Boost ready", "TimedBoost", "Timed MAX", "ConstantMaximum", "CONSTANT MAX", "Automatic", "AUTOMATIC",
                    "PowerProfiles", "Separate AC and battery profiles", "AcPolicy", "On AC", "BatteryPolicy", "On battery", "ImportProfile", "Import", "ExportProfile", "Export",
                    "HardwareSupport", "Hardware support", "FullControl", "Full control", "MonitoringOnly", "Monitoring only", "Fan1", "Fan 1", "Fan2", "Fan 2",
                    "MeasureRpm", "Measure RPM", "HealthTest", "Fan test", "NoMeasurement", "No measurement yet", "StartWithWindows", "Start with Windows",
                    "StartMinimized", "Start minimized", "AlwaysOnTop", "Always on top", "CloseToTray", "Minimize to tray on close", "Notifications", "Show notifications",
                    "Language", "Language", "OpenLog", "Log", "ResetControl", "Reset control", "TelemetryLogging", "Telemetry logging", "OpenTelemetry", "Open CSV",
                    "SettingsStored", "Settings are stored for this user.", "Connecting", "CONNECTING", "Ready", "READY", "ModeMaximum", "MAXIMUM",
                    "ModeAutomatic", "AUTOMATIC", "ModeError", "ERROR", "TwoFansMaximum", "two fans MAX", "FirmwareManaged", "firmware", "OnAc", "On AC",
                    "OnBattery", "On battery", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "Quiet", "ProfileBalanced", "Balanced", "ProfileCool", "Cool",
                    "ProfileConstantMaximum", "Constant MAX", "ProfileCustom", "Custom", "Exit", "Exit", "Show", "Show", "HealthTesting", "Testing fan response…",
                    "HealthPassed", "Both fans responded to the command.", "HealthFailed", "Fan response test failed.", "HealthInconclusive", "Fan test was inconclusive.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: full control", "DeviceMonitoringOnly", "Generic Windows PC: sensor monitoring only",
                    "StartupDelay", "Startup delay", "Seconds", "s", "TabCurve", "Curve", "CurveEnabled", "Enable custom AUTO / MAX curve",
                    "CurveTurnOn", "MAX threshold", "CurveTurnOff", "Return to quiet", "CurveDwell", "Minimum hold", "PredictiveCooling", "Pre-cool on a rapid temperature rise",
                    "CustomCurveStatus", "Custom curve: MAX {0} °C / AUTO {1} °C • {2} s", "SensorAcpi", "ACPI sensor", "SensorUnavailable", "Temperature sensor unavailable",
                    "SensorEc", "EC sensor", "SensorWaiting", "Waiting for EC temperature…", "SensorEcStale", "EC stale: {0:0.0} °C • {1} s • trend {2:+0.0;-0.0;0.0} °C/min",
                    "SensorEcLive", "EC live: {0:0.0} °C • {1} s • trend {2:+0.0;-0.0;0.0} °C/min", "SessionTemperatureRange", "Session min/max: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Firmware AUTO (quiet) below; both fans MAX above."),

                ["de"] = D(
                    "LastRpmMeasurement", "Letzte Drehzahlmessung: {0}", "RpmDataStale", "Keine aktuellen Drehzahldaten",
                    "AppTitle", "Lüftersteuerung", "Header", "LÜFTERSTEUERUNG", "TabControl", "Steuerung", "TabProfiles", "Profile", "TabStatus", "Status", "TabSettings", "Einstellungen",
                    "Temperature", "Temperatur", "Critical", "kritisch", "FanCommand", "Lüfterbefehl", "CpuPower", "CPU / Strom", "Profile", "Profil",
                    "Aggressiveness", "Kühl-Aggressivität", "PolicyAuto", "Automatische Firmware-Lüftersteuerung.", "PolicyMax", "Geprüfter dauerhafter Maximalbetrieb.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; 5 °C Hysterese, 60 s Wartezeit.", "VerifiedPeakRpm", "Geprüfte Spitzen-RPM",
                    "BoostReady", "Boost bereit", "TimedBoost", "MAX mit Zeit", "ConstantMaximum", "DAUER-MAX", "Automatic", "AUTOMATIK",
                    "PowerProfiles", "Getrennte Netz-/Akkuprofile", "AcPolicy", "Netzbetrieb", "BatteryPolicy", "Akkubetrieb", "ImportProfile", "Importieren", "ExportProfile", "Exportieren",
                    "HardwareSupport", "Hardwareunterstützung", "FullControl", "Volle Steuerung", "MonitoringOnly", "Nur Überwachung", "Fan1", "Lüfter 1", "Fan2", "Lüfter 2",
                    "MeasureRpm", "RPM messen", "HealthTest", "Lüftertest", "NoMeasurement", "Noch keine Messung", "StartWithWindows", "Mit Windows starten",
                    "StartMinimized", "Minimiert starten", "AlwaysOnTop", "Immer im Vordergrund", "CloseToTray", "Beim Schließen minimieren", "Notifications", "Benachrichtigungen",
                    "Language", "Sprache", "OpenLog", "Protokoll", "ResetControl", "Steuerung zurücksetzen", "TelemetryLogging", "Telemetrie protokollieren", "OpenTelemetry", "CSV öffnen",
                    "SettingsStored", "Einstellungen werden für diesen Benutzer gespeichert.", "Connecting", "VERBINDUNG", "Ready", "BEREIT", "ModeMaximum", "MAXIMUM",
                    "ModeAutomatic", "AUTOMATIK", "ModeError", "FEHLER", "TwoFansMaximum", "beide Lüfter MAX", "FirmwareManaged", "Firmware", "OnAc", "Netz",
                    "OnBattery", "Akku", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "Leise", "ProfileBalanced", "Ausgeglichen", "ProfileCool", "Kühl",
                    "ProfileConstantMaximum", "Dauer-MAX", "ProfileCustom", "Benutzerdefiniert", "Exit", "Beenden", "Show", "Anzeigen", "HealthTesting", "Lüfterreaktion wird getestet…",
                    "HealthPassed", "Beide Lüfter haben reagiert.", "HealthFailed", "Lüftertest fehlgeschlagen.", "HealthInconclusive", "Lüftertest ohne eindeutiges Ergebnis.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: volle Steuerung", "DeviceMonitoringOnly", "Allgemeiner Windows-PC: nur Sensorüberwachung",
                    "StartupDelay", "Startverzögerung", "Seconds", "s", "TabCurve", "Kurve", "CurveEnabled", "Eigene AUTO-/MAX-Kurve aktivieren",
                    "CurveTurnOn", "MAX-Schwelle", "CurveTurnOff", "Zurück zu leise", "CurveDwell", "Mindesthaltezeit", "PredictiveCooling", "Bei schnellem Anstieg früher kühlen",
                    "CustomCurveStatus", "Eigene Kurve: MAX {0} °C / AUTO {1} °C • {2} s", "SensorAcpi", "ACPI-Sensor", "SensorUnavailable", "Temperatursensor nicht verfügbar",
                    "SensorEc", "EC-Sensor", "SensorWaiting", "Warte auf EC-Temperatur…", "SensorEcStale", "EC veraltet: {0:0.0} °C • {1} s • Trend {2:+0.0;-0.0;0.0} °C/min",
                    "SensorEcLive", "EC live: {0:0.0} °C • {1} s • Trend {2:+0.0;-0.0;0.0} °C/min", "SessionTemperatureRange", "Sitzung Min/Max: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Unten Firmware AUTO (leise), oben beide Lüfter MAX."),

                ["it"] = D(
                    "LastRpmMeasurement", "Ultima misura RPM: {0}", "RpmDataStale", "Nessun dato RPM aggiornato",
                    "AppTitle", "Controllo ventole", "Header", "CONTROLLO VENTOLE", "TabControl", "Controllo", "TabProfiles", "Profili", "TabStatus", "Stato", "TabSettings", "Impostazioni",
                    "Temperature", "Temperatura", "Critical", "critica", "FanCommand", "Comando ventole", "CpuPower", "CPU / Alimentazione", "Profile", "Profilo",
                    "Aggressiveness", "Intensità raffreddamento", "PolicyAuto", "Gestione automatica del firmware.", "PolicyMax", "Massimo continuo verificato.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; isteresi 5 °C, attesa 60 s.", "VerifiedPeakRpm", "RPM di picco verificati",
                    "BoostReady", "Boost pronto", "TimedBoost", "MAX temporaneo", "ConstantMaximum", "MAX CONTINUO", "Automatic", "AUTOMATICO",
                    "PowerProfiles", "Profili separati rete/batteria", "AcPolicy", "Con alimentazione", "BatteryPolicy", "A batteria", "ImportProfile", "Importa", "ExportProfile", "Esporta",
                    "HardwareSupport", "Supporto hardware", "FullControl", "Controllo completo", "MonitoringOnly", "Solo monitoraggio", "Fan1", "Ventola 1", "Fan2", "Ventola 2",
                    "MeasureRpm", "Misura RPM", "HealthTest", "Test ventole", "NoMeasurement", "Nessuna misura", "StartWithWindows", "Avvia con Windows",
                    "StartMinimized", "Avvia ridotto", "AlwaysOnTop", "Sempre in primo piano", "CloseToTray", "Riduci nell'area di notifica", "Notifications", "Mostra notifiche",
                    "Language", "Lingua", "OpenLog", "Registro", "ResetControl", "Ripristina controllo", "TelemetryLogging", "Registra telemetria", "OpenTelemetry", "Apri CSV",
                    "SettingsStored", "Le impostazioni sono salvate per questo utente.", "Connecting", "CONNESSIONE", "Ready", "PRONTO", "ModeMaximum", "MASSIMO",
                    "ModeAutomatic", "AUTOMATICO", "ModeError", "ERRORE", "TwoFansMaximum", "due ventole MAX", "FirmwareManaged", "firmware", "OnAc", "Alimentato",
                    "OnBattery", "Batteria", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "Silenzioso", "ProfileBalanced", "Bilanciato", "ProfileCool", "Fresco",
                    "ProfileConstantMaximum", "MAX continuo", "ProfileCustom", "Personalizzato", "Exit", "Esci", "Show", "Mostra", "HealthTesting", "Test risposta ventole…",
                    "HealthPassed", "Entrambe le ventole hanno risposto.", "HealthFailed", "Test delle ventole fallito.", "HealthInconclusive", "Test delle ventole non conclusivo.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: controllo completo", "DeviceMonitoringOnly", "PC Windows generico: solo monitoraggio sensori",
                    "StartupDelay", "Ritardo avvio", "Seconds", "s", "TabCurve", "Curva", "CurveEnabled", "Attiva curva AUTO / MAX personalizzata",
                    "CurveTurnOn", "Soglia MAX", "CurveTurnOff", "Ritorno silenzioso", "CurveDwell", "Permanenza minima", "PredictiveCooling", "Raffredda prima se la temperatura sale rapidamente",
                    "CustomCurveStatus", "Curva: MAX {0} °C / AUTO {1} °C • {2} s", "SensorAcpi", "Sensore ACPI", "SensorUnavailable", "Sensore temperatura non disponibile",
                    "SensorEc", "Sensore EC", "SensorWaiting", "In attesa della temperatura EC…", "SensorEcStale", "EC obsoleto: {0:0.0} °C • {1} s • tendenza {2:+0.0;-0.0;0.0} °C/min",
                    "SensorEcLive", "EC attivo: {0:0.0} °C • {1} s • tendenza {2:+0.0;-0.0;0.0} °C/min", "SessionTemperatureRange", "Min/max sessione: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Firmware AUTO (silenzioso) sotto; entrambe le ventole MAX sopra."),

                ["es"] = D(
                    "LastRpmMeasurement", "Última lectura de RPM: {0}", "RpmDataStale", "Sin datos actuales de RPM",
                    "AppTitle", "Control de ventiladores", "Header", "CONTROL DE VENTILADORES", "TabControl", "Control", "TabProfiles", "Perfiles", "TabStatus", "Estado", "TabSettings", "Ajustes",
                    "Temperature", "Temperatura", "Critical", "crítica", "FanCommand", "Orden del ventilador", "CpuPower", "CPU / Energía", "Profile", "Perfil",
                    "Aggressiveness", "Intensidad de refrigeración", "PolicyAuto", "Gestión automática del firmware.", "PolicyMax", "Máximo continuo verificado.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; histéresis 5 °C, espera 60 s.", "VerifiedPeakRpm", "RPM máximas verificadas",
                    "BoostReady", "Boost preparado", "TimedBoost", "MAX temporal", "ConstantMaximum", "MAX CONSTANTE", "Automatic", "AUTOMÁTICO",
                    "PowerProfiles", "Perfiles separados para red/batería", "AcPolicy", "Con corriente", "BatteryPolicy", "Con batería", "ImportProfile", "Importar", "ExportProfile", "Exportar",
                    "HardwareSupport", "Compatibilidad de hardware", "FullControl", "Control completo", "MonitoringOnly", "Solo supervisión", "Fan1", "Ventilador 1", "Fan2", "Ventilador 2",
                    "MeasureRpm", "Medir RPM", "HealthTest", "Probar ventiladores", "NoMeasurement", "Sin mediciones", "StartWithWindows", "Iniciar con Windows",
                    "StartMinimized", "Iniciar minimizado", "AlwaysOnTop", "Siempre visible", "CloseToTray", "Minimizar al cerrar", "Notifications", "Mostrar notificaciones",
                    "Language", "Idioma", "OpenLog", "Registro", "ResetControl", "Restablecer control", "TelemetryLogging", "Registrar telemetría", "OpenTelemetry", "Abrir CSV",
                    "SettingsStored", "Los ajustes se guardan para este usuario.", "Connecting", "CONECTANDO", "Ready", "LISTO", "ModeMaximum", "MÁXIMO",
                    "ModeAutomatic", "AUTOMÁTICO", "ModeError", "ERROR", "TwoFansMaximum", "dos ventiladores MAX", "FirmwareManaged", "firmware", "OnAc", "Corriente",
                    "OnBattery", "Batería", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "Silencioso", "ProfileBalanced", "Equilibrado", "ProfileCool", "Fresco",
                    "ProfileConstantMaximum", "MAX constante", "ProfileCustom", "Personalizado", "Exit", "Salir", "Show", "Mostrar", "HealthTesting", "Probando respuesta…",
                    "HealthPassed", "Ambos ventiladores respondieron.", "HealthFailed", "La prueba de ventiladores falló.", "HealthInconclusive", "La prueba no fue concluyente.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: control completo", "DeviceMonitoringOnly", "PC Windows genérico: solo sensores",
                    "StartupDelay", "Retraso de inicio", "Seconds", "s", "TabCurve", "Curva", "CurveEnabled", "Activar curva AUTO / MAX personalizada",
                    "CurveTurnOn", "Umbral MAX", "CurveTurnOff", "Volver a silencio", "CurveDwell", "Espera mínima", "PredictiveCooling", "Enfriar antes si la temperatura sube rápido",
                    "CustomCurveStatus", "Curva: MAX {0} °C / AUTO {1} °C • {2} s", "SensorAcpi", "Sensor ACPI", "SensorUnavailable", "Sensor de temperatura no disponible",
                    "SensorEc", "Sensor EC", "SensorWaiting", "Esperando temperatura EC…", "SensorEcStale", "EC obsoleto: {0:0.0} °C • {1} s • tendencia {2:+0.0;-0.0;0.0} °C/min",
                    "SensorEcLive", "EC activo: {0:0.0} °C • {1} s • tendencia {2:+0.0;-0.0;0.0} °C/min", "SessionTemperatureRange", "Mín/máx de sesión: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Firmware AUTO (silencioso) abajo; ambos ventiladores MAX arriba."),

                ["ar"] = D(
                    "LastRpmMeasurement", "آخر قياس لسرعة الدوران: {0}", "RpmDataStale", "لا توجد بيانات حديثة لسرعة الدوران",
                    "AppTitle", "مركز التحكم بالمراوح", "Header", "التحكم بالمراوح", "TabControl", "التحكم", "TabProfiles", "الملفات", "TabStatus", "الحالة", "TabSettings", "الإعدادات",
                    "Temperature", "درجة الحرارة", "Critical", "حرجة", "FanCommand", "أمر المروحة", "CpuPower", "المعالج / الطاقة", "Profile", "الملف",
                    "Aggressiveness", "شدة التبريد", "PolicyAuto", "إدارة تلقائية بالبرنامج الثابت.", "PolicyMax", "أقصى سرعة مستمرة ومتحقق منها.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C؛ فارق 5 °C وانتظار 60 ث.", "VerifiedPeakRpm", "ذروة RPM المتحقق منها",
                    "BoostReady", "التعزيز جاهز", "TimedBoost", "MAX مؤقت", "ConstantMaximum", "MAX مستمر", "Automatic", "تلقائي",
                    "PowerProfiles", "ملفات منفصلة للطاقة والبطارية", "AcPolicy", "مع الطاقة", "BatteryPolicy", "على البطارية", "ImportProfile", "استيراد", "ExportProfile", "تصدير",
                    "HardwareSupport", "دعم الأجهزة", "FullControl", "تحكم كامل", "MonitoringOnly", "مراقبة فقط", "Fan1", "المروحة 1", "Fan2", "المروحة 2",
                    "MeasureRpm", "قياس RPM", "HealthTest", "اختبار المراوح", "NoMeasurement", "لا يوجد قياس", "StartWithWindows", "بدء مع Windows",
                    "StartMinimized", "بدء مصغراً", "AlwaysOnTop", "دائماً في الأعلى", "CloseToTray", "التصغير عند الإغلاق", "Notifications", "إظهار الإشعارات",
                    "Language", "اللغة", "OpenLog", "السجل", "ResetControl", "إعادة ضبط التحكم", "TelemetryLogging", "تسجيل القياسات", "OpenTelemetry", "فتح CSV",
                    "SettingsStored", "تُحفظ الإعدادات لهذا المستخدم.", "Connecting", "جارٍ الاتصال", "Ready", "جاهز", "ModeMaximum", "أقصى",
                    "ModeAutomatic", "تلقائي", "ModeError", "خطأ", "TwoFansMaximum", "المروحتان MAX", "FirmwareManaged", "البرنامج الثابت", "OnAc", "طاقة",
                    "OnBattery", "بطارية", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "هادئ", "ProfileBalanced", "متوازن", "ProfileCool", "بارد",
                    "ProfileConstantMaximum", "MAX مستمر", "ProfileCustom", "مخصص", "Exit", "خروج", "Show", "إظهار", "HealthTesting", "جارٍ اختبار المراوح…",
                    "HealthPassed", "استجابت المروحتان.", "HealthFailed", "فشل اختبار المراوح.", "HealthInconclusive", "نتيجة الاختبار غير حاسمة.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: تحكم كامل", "DeviceMonitoringOnly", "حاسوب Windows عام: مراقبة فقط",
                    "StartupDelay", "تأخير البدء", "Seconds", "ث", "TabCurve", "المنحنى", "CurveEnabled", "تفعيل منحنى AUTO / MAX المخصص",
                    "CurveTurnOn", "عتبة MAX", "CurveTurnOff", "العودة للهدوء", "CurveDwell", "أقل مدة", "PredictiveCooling", "تبريد مبكر عند الارتفاع السريع",
                    "CustomCurveStatus", "منحنى: MAX {0} °C / AUTO {1} °C • {2} ث", "SensorAcpi", "مستشعر ACPI", "SensorUnavailable", "مستشعر الحرارة غير متاح",
                    "SensorEc", "مستشعر EC", "SensorWaiting", "انتظار حرارة EC…", "SensorEcStale", "EC قديم: {0:0.0} °C • {1} ث • الاتجاه {2:+0.0;-0.0;0.0} °C/د",
                    "SensorEcLive", "EC مباشر: {0:0.0} °C • {1} ث • الاتجاه {2:+0.0;-0.0;0.0} °C/د", "SessionTemperatureRange", "أدنى/أعلى الجلسة: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Firmware AUTO هادئ في الأسفل، والمروحتان MAX في الأعلى."),

                ["zh-Hans"] = D(
                    "LastRpmMeasurement", "上次转速测量：{0}", "RpmDataStale", "暂无最新转速数据",
                    "AppTitle", "风扇控制中心", "Header", "风扇控制中心", "TabControl", "控制", "TabProfiles", "配置", "TabStatus", "状态", "TabSettings", "设置",
                    "Temperature", "温度", "Critical", "临界", "FanCommand", "风扇命令", "CpuPower", "CPU / 电源", "Profile", "配置",
                    "Aggressiveness", "冷却强度", "PolicyAuto", "固件自动风扇管理。", "PolicyMax", "已验证的持续最高风速。",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C；5 °C 滞后，等待 60 秒。", "VerifiedPeakRpm", "已验证峰值 RPM",
                    "BoostReady", "加速已就绪", "TimedBoost", "定时 MAX", "ConstantMaximum", "持续 MAX", "Automatic", "自动",
                    "PowerProfiles", "电源/电池独立配置", "AcPolicy", "接通电源", "BatteryPolicy", "使用电池", "ImportProfile", "导入", "ExportProfile", "导出",
                    "HardwareSupport", "硬件支持", "FullControl", "完全控制", "MonitoringOnly", "仅监控", "Fan1", "风扇 1", "Fan2", "风扇 2",
                    "MeasureRpm", "测量 RPM", "HealthTest", "风扇测试", "NoMeasurement", "暂无测量", "StartWithWindows", "随 Windows 启动",
                    "StartMinimized", "启动时最小化", "AlwaysOnTop", "始终置顶", "CloseToTray", "关闭时最小化到托盘", "Notifications", "显示通知",
                    "Language", "语言", "OpenLog", "日志", "ResetControl", "重置控制", "TelemetryLogging", "记录遥测数据", "OpenTelemetry", "打开 CSV",
                    "SettingsStored", "设置保存于当前用户。", "Connecting", "正在连接", "Ready", "就绪", "ModeMaximum", "最高",
                    "ModeAutomatic", "自动", "ModeError", "错误", "TwoFansMaximum", "两个风扇 MAX", "FirmwareManaged", "固件", "OnAc", "电源",
                    "OnBattery", "电池", "ProfileAutomatic", "固件 AUTO", "ProfileQuiet", "安静", "ProfileBalanced", "均衡", "ProfileCool", "冷却",
                    "ProfileConstantMaximum", "持续 MAX", "ProfileCustom", "自定义", "Exit", "退出", "Show", "显示", "HealthTesting", "正在测试风扇响应…",
                    "HealthPassed", "两个风扇均已响应。", "HealthFailed", "风扇测试失败。", "HealthInconclusive", "风扇测试无明确结果。",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519：完全控制", "DeviceMonitoringOnly", "通用 Windows 电脑：仅监控传感器",
                    "StartupDelay", "启动延迟", "Seconds", "秒", "TabCurve", "曲线", "CurveEnabled", "启用自定义 AUTO / MAX 曲线",
                    "CurveTurnOn", "MAX 阈值", "CurveTurnOff", "恢复安静", "CurveDwell", "最短保持", "PredictiveCooling", "温度快速上升时提前冷却",
                    "CustomCurveStatus", "曲线：MAX {0} °C / AUTO {1} °C • {2} 秒", "SensorAcpi", "ACPI 传感器", "SensorUnavailable", "温度传感器不可用",
                    "SensorEc", "EC 传感器", "SensorWaiting", "正在等待 EC 温度…", "SensorEcStale", "EC 已过期：{0:0.0} °C • {1} 秒 • 趋势 {2:+0.0;-0.0;0.0} °C/分",
                    "SensorEcLive", "EC 实时：{0:0.0} °C • {1} 秒 • 趋势 {2:+0.0;-0.0;0.0} °C/分", "SessionTemperatureRange", "本次最低/最高：{0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "低温使用固件 AUTO（安静），高温两个风扇 MAX。"),

                ["ja"] = D(
                    "LastRpmMeasurement", "最終回転数測定：{0}", "RpmDataStale", "最新の回転数データなし",
                    "AppTitle", "ファン制御センター", "Header", "ファン制御センター", "TabControl", "制御", "TabProfiles", "プロファイル", "TabStatus", "状態", "TabSettings", "設定",
                    "Temperature", "温度", "Critical", "危険", "FanCommand", "ファン命令", "CpuPower", "CPU / 電源", "Profile", "プロファイル",
                    "Aggressiveness", "冷却強度", "PolicyAuto", "ファームウェアの自動制御。", "PolicyMax", "検証済みの連続最大ファン。",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C；5 °C ヒステリシス、60 秒待機。", "VerifiedPeakRpm", "検証済みピーク RPM",
                    "BoostReady", "ブースト準備完了", "TimedBoost", "時間制 MAX", "ConstantMaximum", "連続 MAX", "Automatic", "自動",
                    "PowerProfiles", "AC/バッテリー別プロファイル", "AcPolicy", "AC 電源", "BatteryPolicy", "バッテリー", "ImportProfile", "読み込み", "ExportProfile", "書き出し",
                    "HardwareSupport", "ハードウェア対応", "FullControl", "完全制御", "MonitoringOnly", "監視のみ", "Fan1", "ファン 1", "Fan2", "ファン 2",
                    "MeasureRpm", "RPM 測定", "HealthTest", "ファンテスト", "NoMeasurement", "未測定", "StartWithWindows", "Windows と同時に起動",
                    "StartMinimized", "最小化して起動", "AlwaysOnTop", "常に手前に表示", "CloseToTray", "閉じる時にトレイへ", "Notifications", "通知を表示",
                    "Language", "言語", "OpenLog", "ログ", "ResetControl", "制御をリセット", "TelemetryLogging", "テレメトリー記録", "OpenTelemetry", "CSV を開く",
                    "SettingsStored", "設定はこのユーザーに保存されます。", "Connecting", "接続中", "Ready", "準備完了", "ModeMaximum", "最大",
                    "ModeAutomatic", "自動", "ModeError", "エラー", "TwoFansMaximum", "2 ファン MAX", "FirmwareManaged", "ファームウェア", "OnAc", "AC",
                    "OnBattery", "バッテリー", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "静音", "ProfileBalanced", "バランス", "ProfileCool", "冷却",
                    "ProfileConstantMaximum", "連続 MAX", "ProfileCustom", "カスタム", "Exit", "終了", "Show", "表示", "HealthTesting", "ファン応答をテスト中…",
                    "HealthPassed", "両方のファンが応答しました。", "HealthFailed", "ファンテストに失敗しました。", "HealthInconclusive", "ファンテストは判定不能でした。",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519：完全制御", "DeviceMonitoringOnly", "汎用 Windows PC：センサー監視のみ",
                    "StartupDelay", "起動遅延", "Seconds", "秒", "TabCurve", "曲線", "CurveEnabled", "カスタム AUTO / MAX 曲線を有効化",
                    "CurveTurnOn", "MAX しきい値", "CurveTurnOff", "静音へ戻す", "CurveDwell", "最小保持時間", "PredictiveCooling", "温度の急上昇時に早めに冷却",
                    "CustomCurveStatus", "曲線：MAX {0} °C / AUTO {1} °C • {2} 秒", "SensorAcpi", "ACPI センサー", "SensorUnavailable", "温度センサーを利用できません",
                    "SensorEc", "EC センサー", "SensorWaiting", "EC 温度を待機中…", "SensorEcStale", "EC 更新切れ：{0:0.0} °C • {1} 秒 • 傾向 {2:+0.0;-0.0;0.0} °C/分",
                    "SensorEcLive", "EC ライブ：{0:0.0} °C • {1} 秒 • 傾向 {2:+0.0;-0.0;0.0} °C/分", "SessionTemperatureRange", "セッション最小/最大：{0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "低温では Firmware AUTO（静音）、高温では両ファン MAX。"),

                ["ru"] = D(
                    "LastRpmMeasurement", "Последний замер оборотов: {0}", "RpmDataStale", "Нет свежих данных об оборотах",
                    "AppTitle", "Центр управления вентиляторами", "Header", "УПРАВЛЕНИЕ ВЕНТИЛЯТОРАМИ", "TabControl", "Управление", "TabProfiles", "Профили", "TabStatus", "Статус", "TabSettings", "Настройки",
                    "Temperature", "Температура", "Critical", "критич.", "FanCommand", "Команда", "CpuPower", "CPU / Питание", "Profile", "Профиль",
                    "Aggressiveness", "Интенсивность охлаждения", "PolicyAuto", "Автоматика прошивки.", "PolicyMax", "Проверенный постоянный максимум.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; гистерезис 5 °C, пауза 60 с.", "VerifiedPeakRpm", "Проверенные пиковые RPM",
                    "BoostReady", "Ускорение готово", "TimedBoost", "MAX по таймеру", "ConstantMaximum", "ПОСТОЯННЫЙ MAX", "Automatic", "АВТО",
                    "PowerProfiles", "Разные профили для сети/батареи", "AcPolicy", "От сети", "BatteryPolicy", "От батареи", "ImportProfile", "Импорт", "ExportProfile", "Экспорт",
                    "HardwareSupport", "Поддержка оборудования", "FullControl", "Полное управление", "MonitoringOnly", "Только мониторинг", "Fan1", "Вентилятор 1", "Fan2", "Вентилятор 2",
                    "MeasureRpm", "Измерить RPM", "HealthTest", "Тест вентиляторов", "NoMeasurement", "Нет измерений", "StartWithWindows", "Запускать с Windows",
                    "StartMinimized", "Запускать свёрнутым", "AlwaysOnTop", "Поверх всех окон", "CloseToTray", "Сворачивать в трей", "Notifications", "Показывать уведомления",
                    "Language", "Язык", "OpenLog", "Журнал", "ResetControl", "Сбросить управление", "TelemetryLogging", "Запись телеметрии", "OpenTelemetry", "Открыть CSV",
                    "SettingsStored", "Настройки сохраняются для этого пользователя.", "Connecting", "ПОДКЛЮЧЕНИЕ", "Ready", "ГОТОВО", "ModeMaximum", "МАКСИМУМ",
                    "ModeAutomatic", "АВТО", "ModeError", "ОШИБКА", "TwoFansMaximum", "два вентилятора MAX", "FirmwareManaged", "прошивка", "OnAc", "Сеть",
                    "OnBattery", "Батарея", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "Тихий", "ProfileBalanced", "Сбалансированный", "ProfileCool", "Холодный",
                    "ProfileConstantMaximum", "Постоянный MAX", "ProfileCustom", "Свой", "Exit", "Выход", "Show", "Показать", "HealthTesting", "Проверка отклика…",
                    "HealthPassed", "Оба вентилятора ответили.", "HealthFailed", "Тест вентиляторов не пройден.", "HealthInconclusive", "Тест не дал точного результата.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: полное управление", "DeviceMonitoringOnly", "Обычный Windows-ПК: только датчики",
                    "StartupDelay", "Задержка запуска", "Seconds", "с", "TabCurve", "Кривая", "CurveEnabled", "Включить свою кривую AUTO / MAX",
                    "CurveTurnOn", "Порог MAX", "CurveTurnOff", "Возврат в тихий", "CurveDwell", "Мин. удержание", "PredictiveCooling", "Охлаждать раньше при быстром росте",
                    "CustomCurveStatus", "Кривая: MAX {0} °C / AUTO {1} °C • {2} с", "SensorAcpi", "Датчик ACPI", "SensorUnavailable", "Датчик температуры недоступен",
                    "SensorEc", "Датчик EC", "SensorWaiting", "Ожидание температуры EC…", "SensorEcStale", "EC устарел: {0:0.0} °C • {1} с • тренд {2:+0.0;-0.0;0.0} °C/мин",
                    "SensorEcLive", "EC: {0:0.0} °C • {1} с • тренд {2:+0.0;-0.0;0.0} °C/мин", "SessionTemperatureRange", "Мин/макс сеанса: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Внизу Firmware AUTO (тихо), вверху оба вентилятора MAX."),

                ["pl"] = D(
                    "LastRpmMeasurement", "Ostatni pomiar obrotów: {0}", "RpmDataStale", "Brak aktualnych danych o obrotach",
                    "AppTitle", "Centrum sterowania wentylatorami", "Header", "STEROWANIE WENTYLATORAMI", "TabControl", "Sterowanie", "TabProfiles", "Profile", "TabStatus", "Stan", "TabSettings", "Ustawienia",
                    "Temperature", "Temperatura", "Critical", "krytyczna", "FanCommand", "Polecenie wentylatora", "CpuPower", "CPU / Zasilanie", "Profile", "Profil",
                    "Aggressiveness", "Intensywność chłodzenia", "PolicyAuto", "Automatyczne sterowanie firmware.", "PolicyMax", "Zweryfikowana stała prędkość maksymalna.",
                    "PolicyCurve", "MAX {0:0.0} °C / AUTO {1:0.0} °C; histereza 5 °C, oczekiwanie 60 s.", "VerifiedPeakRpm", "Zweryfikowane szczytowe RPM",
                    "BoostReady", "Boost gotowy", "TimedBoost", "MAX czasowy", "ConstantMaximum", "STAŁY MAX", "Automatic", "AUTOMATYCZNY",
                    "PowerProfiles", "Osobne profile sieci/baterii", "AcPolicy", "Zasilanie sieciowe", "BatteryPolicy", "Na baterii", "ImportProfile", "Importuj", "ExportProfile", "Eksportuj",
                    "HardwareSupport", "Obsługa sprzętu", "FullControl", "Pełne sterowanie", "MonitoringOnly", "Tylko monitoring", "Fan1", "Wentylator 1", "Fan2", "Wentylator 2",
                    "MeasureRpm", "Zmierz RPM", "HealthTest", "Test wentylatorów", "NoMeasurement", "Brak pomiaru", "StartWithWindows", "Uruchamiaj z Windows",
                    "StartMinimized", "Uruchamiaj zminimalizowany", "AlwaysOnTop", "Zawsze na wierzchu", "CloseToTray", "Minimalizuj do zasobnika", "Notifications", "Pokazuj powiadomienia",
                    "Language", "Język", "OpenLog", "Dziennik", "ResetControl", "Resetuj sterowanie", "TelemetryLogging", "Zapisuj telemetrię", "OpenTelemetry", "Otwórz CSV",
                    "SettingsStored", "Ustawienia są zapisane dla tego użytkownika.", "Connecting", "ŁĄCZENIE", "Ready", "GOTOWY", "ModeMaximum", "MAKSIMUM",
                    "ModeAutomatic", "AUTOMATYCZNY", "ModeError", "BŁĄD", "TwoFansMaximum", "dwa wentylatory MAX", "FirmwareManaged", "firmware", "OnAc", "Sieć",
                    "OnBattery", "Bateria", "ProfileAutomatic", "Firmware AUTO", "ProfileQuiet", "Cichy", "ProfileBalanced", "Zrównoważony", "ProfileCool", "Chłodny",
                    "ProfileConstantMaximum", "Stały MAX", "ProfileCustom", "Własny", "Exit", "Wyjście", "Show", "Pokaż", "HealthTesting", "Testowanie wentylatorów…",
                    "HealthPassed", "Oba wentylatory odpowiedziały.", "HealthFailed", "Test wentylatorów nie powiódł się.", "HealthInconclusive", "Test nie dał jednoznacznego wyniku.",
                    "DeviceSupportedSurface", "Surface Pro 1 / IT8519: pełne sterowanie", "DeviceMonitoringOnly", "Ogólny komputer Windows: tylko czujniki",
                    "StartupDelay", "Opóźnienie startu", "Seconds", "s", "TabCurve", "Krzywa", "CurveEnabled", "Włącz własną krzywą AUTO / MAX",
                    "CurveTurnOn", "Próg MAX", "CurveTurnOff", "Powrót do ciszy", "CurveDwell", "Min. podtrzymanie", "PredictiveCooling", "Chłodź wcześniej przy szybkim wzroście",
                    "CustomCurveStatus", "Krzywa: MAX {0} °C / AUTO {1} °C • {2} s", "SensorAcpi", "Czujnik ACPI", "SensorUnavailable", "Czujnik temperatury niedostępny",
                    "SensorEc", "Czujnik EC", "SensorWaiting", "Oczekiwanie na temperaturę EC…", "SensorEcStale", "EC nieaktualny: {0:0.0} °C • {1} s • trend {2:+0.0;-0.0;0.0} °C/min",
                    "SensorEcLive", "EC: {0:0.0} °C • {1} s • trend {2:+0.0;-0.0;0.0} °C/min", "SessionTemperatureRange", "Min/maks sesji: {0:0.0} / {1:0.0} °C",
                    "CurveAutoMaxHint", "Poniżej Firmware AUTO (cicho), powyżej oba wentylatory MAX.")
            };

        public static IReadOnlyList<SupportedLanguage> SupportedLanguages => LanguageList;
        public static IReadOnlyCollection<string> Keys => Translations["en"].Keys.ToArray();

        public static SupportedLanguage Language(string code)
        {
            var normalized = NormalizeLanguageCode(code);
            return LanguageList.First(language =>
                string.Equals(language.Code, normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static string Get(string languageCode, string key)
        {
            var normalized = NormalizeLanguageCode(languageCode);
            if (!Translations[normalized].TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException("Unknown localization key: " + key);
            }
            return value;
        }

        public static string NormalizeLanguageCode(string? code)
        {
            var candidate = (code ?? string.Empty).Trim();
            if (candidate.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }
            foreach (var language in LanguageList)
            {
                if (candidate.Equals(language.Code, StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(language.Code + "-", StringComparison.OrdinalIgnoreCase))
                {
                    return language.Code;
                }
            }
            return "en";
        }

        private static IReadOnlyDictionary<string, string> D(params string[] pairs)
        {
            if (pairs.Length % 2 != 0)
            {
                throw new InvalidOperationException("Localization entries must be key/value pairs.");
            }
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < pairs.Length; index += 2)
            {
                result.Add(pairs[index], pairs[index + 1]);
            }
            return result;
        }
    }
}
