// L'aggiornamento con un clic. All'avvio la mod guarda le release di github.com/LeoBacca/optcg-trainer-mod: se ce n'è una
// più nuova con il file OPTCGTrainer.dll, la riga sopra il pulsante OPTCG TRAINER dice "Update to x.y.z: click here".
// Il clic scarica solo quel file, lo controlla (l'impronta sha256 che dà GitHub, e dentro il dll la versione della release)
// e lo mette al posto del vecchio: il sim lascia sostituire il dll anche mentre gira, la versione nuova parte al prossimo
// avvio. Il dll vecchio resta in BepInEx\updates (per tornare indietro a mano). BepInEx e winhttp.dll non si toccano.
// Pubblicare una release con OPTCGTrainer.dll = mandare l'aggiornamento a tutti (tools\pacchetto.ps1 lo prepara).
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace OPTCGTrainer
{
    static class Updater
    {
        const string Releases = "https://api.github.com/repos/LeoBacca/optcg-trainer-mod/releases?per_page=20";
        const string AssetName = "OPTCGTrainer.dll";
        // il dll si prende solo dalle release di questo repo (l'impronta dice che il file è quello pubblicato, non chi l'ha fatto)
        const string Downloads = "https://github.com/LeoBacca/optcg-trainer-mod/releases/download/";

        enum State { None, Available, Downloading, Ready, Failed }
        static State state;
        static Version newVersion;
        static string url, sha256;

        internal static void Init()
        {
            if (Plugin.CheckUpdates.Value) Plugin.Instance.StartCoroutine(Check());
        }

        /// La riga per il menu (null = niente da dire, resta lo stato del collegamento).
        internal static string Text => state switch
        {
            State.Available => $"<b><color={Look.GoldHex}>Update to {newVersion}: click here</color></b>",
            State.Downloading => $"Downloading {newVersion}...",
            State.Ready => $"<b><color={Look.GoldHex}>Updated to {newVersion}. Restart OPTCGSim to finish</color></b>",
            State.Failed => "Update failed. Click here to try again",
            _ => null,
        };

        /// true se la riga è da cliccare (c'è un aggiornamento da scaricare, o da riprovare).
        internal static bool Clickable => state == State.Available || state == State.Failed;

        internal static void Install()
        {
            if (Clickable) Plugin.Instance.StartCoroutine(Download());
        }

        static IEnumerator Check()
        {
            using var web = UnityWebRequest.Get(ReleasesUrl());
            web.timeout = 20;
            web.SetRequestHeader("Accept", "application/vnd.github+json");
            yield return web.SendWebRequest();
            if (web.result != UnityWebRequest.Result.Success) { Plugin.Log.LogInfo("aggiornamenti: GitHub non risponde (" + web.error + ")"); yield break; }
            try { Pick(JArray.Parse(web.downloadHandler.text)); }
            catch (Exception e) { Plugin.Log.LogWarning("aggiornamenti: risposta di GitHub non letta: " + e.Message); }
        }

        // la release più nuova (anche le beta) che abbia il dll con la sua impronta; le bozze GitHub non le mostra a nessuno
        static void Pick(JArray releases)
        {
            var best = Parse(Plugin.Version);
            foreach (var release in releases.OfType<JObject>())
            {
                var version = Parse((string)release["tag_name"]);
                if ((bool?)release["draft"] == true || version == null || version <= best) continue;
                var asset = (release["assets"] as JArray)?.OfType<JObject>().FirstOrDefault(a => (string)a["name"] == AssetName);
                var digest = (string)asset?["digest"];
                var link = (string)asset?["browser_download_url"];
                if (digest == null || !digest.StartsWith("sha256:") || link == null) continue;
                if (!link.StartsWith(Downloads, StringComparison.Ordinal) && !DevCommands.Enabled) continue;
                best = newVersion = version;
                url = link;
                sha256 = digest.Substring("sha256:".Length).ToLowerInvariant();
            }
            if (newVersion != null) { state = State.Available; Plugin.Log.LogInfo($"aggiornamenti: c'è la {newVersion} ({url})"); }
            else Plugin.Log.LogInfo("aggiornamenti: la " + Plugin.Version + " è la più nuova");
        }

        static IEnumerator Download()
        {
            state = State.Downloading;
            // GitHub rinvia i file delle release al suo archivio: qui i rinvii si seguono (niente token in questa richiesta)
            using var web = UnityWebRequest.Get(url);
            web.timeout = 120;
            yield return web.SendWebRequest();
            if (web.result != UnityWebRequest.Result.Success) { Fail("download: " + web.error); yield break; }
            try
            {
                Replace(web.downloadHandler.data);
                state = State.Ready;
                Plugin.Log.LogInfo($"aggiornamenti: installata la {newVersion}, parte al prossimo avvio");
            }
            catch (Exception e) { Fail(e.Message); }
        }

        static void Replace(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() != sha256)
                    throw new Exception("l'impronta del file non è quella di GitHub");

            // fuori da plugins (BepInEx carica ogni .dll che trova lì), ma sullo stesso disco: gli spostamenti non si fermano a metà
            var dir = Directory.CreateDirectory(Path.Combine(Paths.BepInExRootPath, "updates")).FullName;
            var fresh = Path.Combine(dir, $"OPTCGTrainer-{newVersion}.dll");
            // sul disco davvero prima di spostarlo: se va via la corrente subito dopo, al posto del dll non resta un file vuoto
            using (var fs = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            // un dll sbagliato nella release farebbe chiedere l'aggiornamento a ogni avvio: si controlla la versione che ha dentro
            var inside = AssemblyName.GetAssemblyName(fresh).Version;
            if (inside == null || new Version(inside.Major, inside.Minor, inside.Build) != Normal(newVersion))
            {
                File.Delete(fresh);
                throw new Exception($"dentro il file c'è la versione {inside}, non la {newVersion}");
            }

            var current = Plugin.Instance.Info.Location;
            var old = Path.Combine(dir, $"OPTCGTrainer-{Plugin.Version}.dll");
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try { File.Move(fresh, current); }
            catch { File.Move(old, current); throw; }
            // di copie vecchie ne basta una: quella di prima di questo aggiornamento
            foreach (var f in Directory.GetFiles(dir, "OPTCGTrainer-*.dll"))
                if (!string.Equals(f, old, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
        }

        static void Fail(string why)
        {
            state = State.Failed;
            Plugin.Log.LogWarning("aggiornamenti: non riuscito: " + why);
        }

        static Version Parse(string tag) => Version.TryParse((tag ?? "").TrimStart('v', 'V'), out var v) ? Normal(v) : null;

        // 0.3.4 e 0.3.4.0 sono la stessa versione
        static Version Normal(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

        static string ReleasesUrl()
        {
#if DEVTOOLS
            // nelle prove le release possono arrivare da un server locale (l'indirizzo sta in questo file)
            var file = Path.Combine(Paths.ConfigPath, "optcgtrainer-updates.txt");
            if (DevCommands.Enabled && File.Exists(file)) return File.ReadAllText(file).Trim();
#endif
            return Releases;
        }
    }
}
