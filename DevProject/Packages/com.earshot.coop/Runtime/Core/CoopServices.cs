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
        public static string PlayerId =>
            AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn
                ? AuthenticationService.Instance.PlayerId
                : string.Empty;

        public static bool IsSignedIn =>
            AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;

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
        /// Gibt jeder Editor-Instanz eine eigene Anmelde-Identitaet.
        /// <para>
        /// Ohne das melden sich im Multiplayer Play Mode alle Instanzen mit demselben Konto
        /// an, und die zuletzt gestartete wirft die vorherige aus der Sitzung. Der Multiplayer
        /// Play Mode legt fuer jede virtuelle Instanz einen eigenen Projektordner an, weshalb
        /// sich der Pfad zu den Projektdaten als stabiles Unterscheidungsmerkmal eignet -
        /// ohne dass wir eine Abhaengigkeit auf das Play-Mode-Paket brauchen.
        /// </para>
        /// </summary>
        private static void ApplyInstanceProfile()
        {
            if (!Application.isEditor) return;
            if (!CoopSettings.Instance.UniqueProfilePerEditorInstance) return;

            string profile = BuildProfileName(Application.dataPath);

            try
            {
                AuthenticationService.Instance.SwitchProfile(profile);
                CoopLog.Info($"Anmeldeprofil dieser Editor-Instanz: {profile}");
            }
            catch (Exception ex)
            {
                CoopLog.Warn(
                    $"Anmeldeprofil '{profile}' konnte nicht gesetzt werden ({ex.Message}). " +
                    "Falls mehrere Editor-Instanzen gleichzeitig laufen, kann es sein, dass " +
                    "sie sich gegenseitig aus der Sitzung werfen.");
            }
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
