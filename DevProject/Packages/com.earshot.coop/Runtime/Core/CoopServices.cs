using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Earshot
{
    /// <summary>
    /// Startet Unity Gaming Services und meldet den Spieler anonym an. Alles Weitere
    /// (Sitzungen, Relay, Vivox) setzt darauf auf, deshalb laeuft jeder Verbindungsversuch
    /// zuerst hier durch.
    /// </summary>
    public static class CoopServices
    {
        private static Task readyTask;

        /// <summary>
        /// Anonyme Spieler-ID von Unity Gaming Services. Dieselbe ID kennen sowohl Netcode
        /// als auch Vivox, weshalb sie der Schluessel ist, um eine Stimme dem richtigen
        /// Avatar zuzuordnen. Vor der Anmeldung leer.
        /// </summary>
        public static string PlayerId
        {
            get
            {
                try
                {
                    if (UnityServices.State != ServicesInitializationState.Initialized)
                    {
                        return string.Empty;
                    }

                    return AuthenticationService.Instance != null &&
                           AuthenticationService.Instance.IsSignedIn
                        ? AuthenticationService.Instance.PlayerId
                        : string.Empty;
                }
                catch (ServicesInitializationException)
                {
                    return string.Empty;
                }
            }
        }

        public static bool IsSignedIn
        {
            get
            {
                try
                {
                    if (UnityServices.State != ServicesInitializationState.Initialized)
                    {
                        return false;
                    }

                    return AuthenticationService.Instance != null &&
                           AuthenticationService.Instance.IsSignedIn;
                }
                catch (ServicesInitializationException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Sorgt dafuer, dass die Dienste laufen und der Spieler angemeldet ist.
        /// Mehrfachaufrufe sind unbedenklich: Sie warten auf denselben Vorgang.
        /// </summary>
        public static Task EnsureReadyAsync()
        {
            if (readyTask == null || readyTask.IsFaulted || readyTask.IsCanceled)
            {
                readyTask = InitializeAndSignInAsync();
            }

            return readyTask;
        }

        private static async Task InitializeAndSignInAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    CoopLog.Info("Unity Gaming Services werden initialisiert.");
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    ApplyInstanceProfile();

                    CoopLog.Info("Anonyme Anmeldung laeuft.");
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                CoopLog.Info($"Angemeldet als {AuthenticationService.Instance.PlayerId}.");
            }
            catch (ServicesInitializationException ex)
            {
                CoopLog.Error(
                    "Unity Gaming Services konnten nicht gestartet werden. Meist ist das " +
                    "Projekt nicht mit einem UGS-Projekt verknuepft. " +
                    "Pruefen ueber Tools > Earshot > Setup oder " +
                    "Project Settings > Services.");
                CoopLog.Exception("Initialisierung fehlgeschlagen", ex);
                throw;
            }
            catch (AuthenticationException ex)
            {
                CoopLog.Exception("Anmeldung fehlgeschlagen", ex);
                throw;
            }
            catch (RequestFailedException ex)
            {
                CoopLog.Exception("Anfrage an Unity Gaming Services fehlgeschlagen", ex);
                throw;
            }
        }

        /// <summary>
        /// Gibt dieser Instanz eine eigene Anmelde-Identitaet, falls noetig.
        /// <para>
        /// Zwei Kopien derselben EXE auf demselben PC teilen sich sonst denselben
        /// zwischengespeicherten Login (gleicher Datenordner, gleiches Konto) - die zweite
        /// wirft dann die erste aus der Sitzung, oder beide erscheinen fuer Vivox als
        /// derselbe Sprecher. Ein <c>-profile NAME</c>-Kommandozeilenargument beim Start
        /// loest das explizit, auch in einem fertigen Build: Verknuepfung einmal ohne
        /// Zusatz starten (Spieler 1), eine zweite mit <c>-profile p2</c> am Ende des
        /// "Ziel"-Felds (Spieler 2). Im Editor tritt ohne dieses Argument automatisch der
        /// Multiplayer Play Mode-Fall ein: Jede virtuelle Instanz hat einen eigenen
        /// Projektordner, dessen Pfad als stabiles Unterscheidungsmerkmal dient.
        /// </para>
        /// </summary>
        private static void ApplyInstanceProfile()
        {
            string explicitProfile = ReadProfileArgument();
            if (!string.IsNullOrEmpty(explicitProfile))
            {
                SwitchProfile(explicitProfile, "Kommandozeile -profile");
                return;
            }

            if (!Application.isEditor) return;
            if (!CoopSettings.Instance.UniqueProfilePerEditorInstance) return;

            SwitchProfile(BuildProfileName(Application.dataPath), "Editor-Instanz");
        }

        private static void SwitchProfile(string profile, string reason)
        {
            try
            {
                AuthenticationService.Instance.SwitchProfile(profile);
                CoopLog.Info($"Anmeldeprofil dieser Instanz ({reason}): {profile}");
            }
            catch (Exception ex)
            {
                CoopLog.Warn(
                    $"Anmeldeprofil '{profile}' konnte nicht gesetzt werden ({ex.Message}). " +
                    "Falls mehrere Instanzen gleichzeitig laufen, kann es sein, dass " +
                    "sie sich gegenseitig aus der Sitzung werfen.");
            }
        }

        /// <summary>
        /// Liest <c>-profile NAME</c> aus den Startargumenten. Fuer den zweiten Fenster
        /// beim Selbsttest auf einem PC gedacht - siehe <see cref="ApplyInstanceProfile"/>.
        /// </summary>
        private static string ReadProfileArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-profile", StringComparison.OrdinalIgnoreCase))
                {
                    return SanitizeProfileName(args[i + 1]);
                }
            }

            return null;
        }

        private static string SanitizeProfileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var sb = new StringBuilder(30);
            for (int i = 0; i < raw.Length && sb.Length < 30; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Baut aus einem beliebigen Text einen gueltigen Profilnamen. Unity erlaubt nur
        /// Buchstaben, Ziffern, Bindestrich und Unterstrich bei maximal 30 Zeichen.
        /// </summary>
        internal static string BuildProfileName(string seed)
        {
            unchecked
            {
                // FNV-1a: kurz, deterministisch und ohne Allokationen abseits des Strings.
                const uint offset = 2166136261;
                const uint prime = 16777619;

                uint hash = offset;
                for (int i = 0; i < seed.Length; i++)
                {
                    hash ^= seed[i];
                    hash *= prime;
                }

                var sb = new StringBuilder("editor_", 16);
                sb.Append(hash.ToString("x8"));
                return sb.ToString();
            }
        }
    }
}
