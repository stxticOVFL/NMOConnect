using System.Diagnostics;
using System.Runtime.CompilerServices;
using MelonLoader;
using NeonLite.Modules;
using UnityEngine;
using UnityEngine.Networking;

namespace NMOConnect
{
    public class NMOConnect : MelonMod
    {
        internal static NMOConnect i;

#if DEBUG
        internal static bool DEBUG { get { return Settings.debug.Value; } }
#else
        internal const bool DEBUG = false;
#endif

        internal static string Version { get; private set; }

        internal static MelonLogger.Instance Log => i.LoggerInstance;

        internal static Localization.LocaleCategory LC;

        static Game _gamecache = null;
        internal static Game Game { get { _gamecache ??= Singleton<Game>.Instance; return _gamecache; } }

        public override void OnInitializeMelon()
        {
            i = this;
            Version = Info.Version;

            // Register the settings
            Settings.Register();

            if (!Settings.enabled.Value)
                return;
#if DEBUG
            NeonLite.Modules.Anticheat.Register(MelonAssembly);
#endif
            // Load all modules tagged with the IModule interface
            NeonLite.NeonLite.LoadModules(MelonAssembly);

            const string URL = "https://raw.githubusercontent.com/stxticOVFL/NMOConnect/master/Resources/locale.csv";
            LC = Localization.GetLocale_Stream("NMOConnect", Localization.Reader_CSVStream,
                Resources.locale.GetStream(), URL);
        }

        public override void OnLateInitializeMelon()
        {
            if (Settings.enabled.Value)
                Game.OnInitializationComplete += OnInitComplete;
        }

        internal void OnInitComplete()
        {
            Game.OnInitializationComplete -= OnInitComplete;

            Online.Login();
        }

        internal static class Settings
        {
            public const string h = "NMOConnect";

#if DEBUG
            public static MelonPreferences_Entry<bool> debug;
#endif

            public static MelonPreferences_Entry<bool> enabled;

            public static MelonPreferences_Entry<string> username;
            public static MelonPreferences_Entry<string> password;

            public static MelonPreferences_Entry<bool> always;

            public static void Register()
            {
                NeonLite.Settings.AddHolder(h);

#if DEBUG
                debug = NeonLite.Settings.Add(h, "", "debug", "Debug Mode", null, true, true);
#endif

                enabled = NeonLite.Settings.Add(h, "", "enabled", "Enabled", "Restart required!", false);
                username = NeonLite.Settings.Add(h, "", "username", "Username", "Your NMO user username.", "");
                password = NeonLite.Settings.Add(h, "", "password", "Password",
                    """
                    Your NMO user password.
                    **NOTE THAT THIS IS STORED IN PLAINTEXT IN YOUR PREFERENCES FILE!!**
                    Ping stxticOVFL for a reset.
                    """, "");
                // password.OnEntryValueChanged.Subscribe((_1, _2) => Online.Login());

                always = NeonLite.Settings.Add(h, "", "always", "Tournament-less Tracking",
                """
                Track the duel to view using NMO Bot later even if you have no active tournaments.
                All users must have this setting enabled for the duel to be tracked.
                Keep track of the ID in the sidebar and use /duels info [ID] to view them in Discord.
                """, true);
            }
        }
    }

    internal static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string FpLn([CallerFilePath] string fp = "", [CallerLineNumber] int ln = 0) => $"[{Path.GetFileName(fp)}:{ln}]";

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugMsg(this MelonLogger.Instance log, string msg, [CallerFilePath] string fp = "", [CallerLineNumber] int ln = 0)
        {
            if (NMOConnect.DEBUG)
            {
                log.Msg($"{FpLn(fp, ln)} {msg}");
                // UnityEngine.Debug.Log($"[NMOConnect] {FpLn(fp, ln)} {msg}");
            }
        }

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugMsg(this MelonLogger.Instance log, object obj, [CallerFilePath] string fp = "", [CallerLineNumber] int ln = 0)
            => DebugMsg(log, obj.ToString(), fp, ln);
    }
}
