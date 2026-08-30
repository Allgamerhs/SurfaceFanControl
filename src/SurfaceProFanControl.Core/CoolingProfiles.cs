using System;
using System.Collections.Generic;

namespace SurfaceProFanControl.Core
{
    public enum CoolingProfileId
    {
        Automatic,
        Quiet,
        Balanced,
        Cool,
        ConstantMaximum,
        Custom
    }

    public readonly struct CoolingProfile
    {
        public CoolingProfile(CoolingProfileId id, int aggressivenessPercent, string localizationKey)
        {
            if (aggressivenessPercent < 0 || aggressivenessPercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(aggressivenessPercent));
            }

            Id = id;
            AggressivenessPercent = aggressivenessPercent;
            LocalizationKey = localizationKey ?? throw new ArgumentNullException(nameof(localizationKey));
        }

        public CoolingProfileId Id { get; }
        public int AggressivenessPercent { get; }
        public string LocalizationKey { get; }
    }

    public static class CoolingProfileCatalog
    {
        private static readonly CoolingProfile[] Profiles =
        {
            new CoolingProfile(CoolingProfileId.Automatic, 0, "ProfileAutomatic"),
            new CoolingProfile(CoolingProfileId.Quiet, 20, "ProfileQuiet"),
            new CoolingProfile(CoolingProfileId.Balanced, 50, "ProfileBalanced"),
            new CoolingProfile(CoolingProfileId.Cool, 80, "ProfileCool"),
            new CoolingProfile(CoolingProfileId.ConstantMaximum, 100, "ProfileConstantMaximum")
        };

        public static IReadOnlyList<CoolingProfile> BuiltIn => Profiles;

        public static CoolingProfile Get(CoolingProfileId id)
        {
            foreach (var profile in Profiles)
            {
                if (profile.Id == id)
                {
                    return profile;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(id), "Custom profiles do not have a fixed catalog value.");
        }

        public static CoolingProfileId Match(int aggressivenessPercent)
        {
            foreach (var profile in Profiles)
            {
                if (profile.AggressivenessPercent == aggressivenessPercent)
                {
                    return profile.Id;
                }
            }

            return CoolingProfileId.Custom;
        }

        public static int EffectiveAggressiveness(
            int generalPercent,
            bool usePowerProfiles,
            int acPercent,
            int batteryPercent,
            bool onAcPower)
        {
            ValidatePercent(generalPercent, nameof(generalPercent));
            ValidatePercent(acPercent, nameof(acPercent));
            ValidatePercent(batteryPercent, nameof(batteryPercent));
            return usePowerProfiles ? (onAcPower ? acPercent : batteryPercent) : generalPercent;
        }

        private static void ValidatePercent(int value, string parameterName)
        {
            if (value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
