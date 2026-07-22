using System.ComponentModel;
using System.Text;
using MelonLoader.TinyJSON;
using NMOConnect.Modules;
using Steamworks;
using UnityEngine.Networking;
using static NMOConnect.Modules.NetworkOverride;

namespace NMOConnect
{
    internal static class Online
    {
        const string URL = "https://nw.darksoilt.com/api/v1";

        static string currentToken = null;
        internal static string userID = null;

        internal static CSteamID steamID;

        internal static class Requests
        {
            internal class BaseRequest
            {
                public string ToJSON()
                {
                    // all nulls should be omitted so we have to do some weird shit
                    var temp = new Dictionary<string, object>();

                    foreach (var field in GetType().GetFields())
                    {
                        var val = field.GetValue(this);
                        if (val == null)
                            continue;

                        temp.Add(field.Name, val);
                    }

                    return JSON.Dump(temp, EncodeOptions.NoTypeHints);
                }
            }

            internal class UserLogin : BaseRequest
            {
                public string steam_id;
                public string username;
                public string pass;
            }

            internal class DuelCreate : BaseRequest
            {
                public string level_id;
                public string[] users;
                public double length;
                public string tourney_id;
            }

            internal class DuelTime : BaseRequest
            {
                public double igt;
                public double rta;
                public string time;
                public bool? final;
            }

            internal class DuelDeleteRunner : BaseRequest
            {
                public string steam_id;
            }
        }


        public static void SetupHeaders(UnityWebRequest req, bool auth)
        {
            if (auth)
                req.SetRequestHeader("Authorization", $"Bearer {currentToken}");

            req.SetRequestHeader("User-Agent", $"NMOConnect/{NMOConnect.Version}");
        }

        public static void Login(Action<UnityWebRequest> callback = null)
        {
            if (callback == null) {
                callback = req =>
                {
                    if (req.result == UnityWebRequest.Result.Success)
                        NMOConnect.Log.Msg("Logged into NMOConnect!");
                    else
                        NMOConnect.Log.Error("Failed to log into NMOConnect.");
                };
            }

            steamID = SteamUser.GetSteamID();
            var login = new Requests.UserLogin()
            {
                steam_id = steamID.m_SteamID.ToString(),
                username = NMOConnect.Settings.username.Value,
                pass = NMOConnect.Settings.password.Value
            };

            Post("/users/login", login, req =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var variant = JSON.Load(req.downloadHandler.text);
                    currentToken = variant["token"];
                    userID = variant["user_id"];
                    if (buildText && !buildText.text.Contains("|"))
                        buildText.text = $"NMO User ID: {userID} | " + buildText.text;
                }
                else
                {
                    currentToken = null;
                    userID = null;
                    if (buildText && buildText.text.Contains("|"))
                        buildText.text = buildText.text
                        .Split([" | "], StringSplitOptions.None)[1];
                }

                callback.Invoke(req);
            }, false);
        }

        static void MakeRequest(string method, string route, Requests.BaseRequest data, Action<UnityWebRequest> callback, bool auth = true, bool _tried = false)
        {
            NMOConnect.Log.DebugMsg($"{method} {route} AUTH {auth}");

            if (auth && currentToken == null)
            {
                // we have to login first
                Login(req =>
                {
                    if (req.result == UnityWebRequest.Result.Success)
                        Post(route, data, callback, true);
                    else
                        callback?.Invoke(req);
                });
                return;
            }

            var req = new UnityWebRequest(URL + route, method, new DownloadHandlerBuffer(), null);
            if (data != null)
            {
                NMOConnect.Log.DebugMsg(data.ToJSON());
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data.ToJSON()))
                {
                    contentType = "application/json"
                };
            }
            SetupHeaders(req, auth);

            var res = req.SendWebRequest();
            res.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.ProtocolError && req.responseCode == 401 && !_tried)
                {
                    // our token expired or smth, try again
                    Login(req =>
                    {
                        if (req.result == UnityWebRequest.Result.Success)
                            MakeRequest(method, route, data, callback, true, true);
                        else
                            callback?.Invoke(req);
                    });
                    return;
                }

                NMOConnect.Log.DebugMsg($"{method} {route} RESULT {req.responseCode}");
                NMOConnect.Log.DebugMsg(req.downloadHandler.text);

                callback?.Invoke(req);
            };
        }

        public static void Post(string route, Requests.BaseRequest data, Action<UnityWebRequest> callback, bool auth = true)
            => MakeRequest("POST", route, data, callback, auth);

        public static void Get(string route, Action<UnityWebRequest> callback, Requests.BaseRequest data = null, bool auth = true)
            => MakeRequest("GET", route, data, callback, auth);

        public static void Delete(string route, Action<UnityWebRequest> callback, Requests.BaseRequest data = null, bool auth = true)
            => MakeRequest("DELETE", route, data, callback, auth);
    }
}
