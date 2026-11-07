using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace WildfireAudioEditor
{
    internal sealed class LicenseException : Exception
    {
        public readonly string Code;
        public LicenseException(string code, string message) : base(message) { Code = code; }
    }

    internal sealed class LicenseState
    {
        public bool Valid;
        public string Plan = "";
        public string Label = "未激活";
        public long? ExpiresAt;
        public string Message = "请输入小程序签发的 GG 卡密。";
        public LicenseState Copy() { return (LicenseState)MemberwiseClone(); }
    }

    internal sealed class LicenseReply
    {
        public int Status;
        public string Body;
        public int RetryMilliseconds;
    }

    internal sealed class OnlineLicense
    {
        public const string ProductId = "yuansheng-workshop-v7";
        public const string ApiBase = "https://cloudbase-d1g57v401e050f57e-1495042474.ap-shanghai.app.tcloudbase.com/license";
        private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTWXYZ";
        private static readonly byte[] KeyHeader = { 0x30, 0x2e, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x04, 0x22, 0x04, 0x20 };
        private static readonly HttpClient Http = CreateHttp();
        private readonly string storageDirectory;
        private readonly GgSafeStorage safeStorage;
        private readonly Func<long> now;
        private readonly Func<string, byte[], Dictionary<string, string>, Task<LicenseReply>> transport;
        private readonly Func<int, Task> delay;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private byte[] seed;
        private string publicKey;
        private Dictionary<string, object> record;
        private volatile LicenseState state = new LicenseState();
        private long checkedAt;
        private readonly System.Diagnostics.Stopwatch sinceCheck = new System.Diagnostics.Stopwatch();

        public OnlineLicense() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YSGF7-GG"), null, null, null) { }

        internal OnlineLicense(string directory, Func<string, byte[], Dictionary<string, string>, Task<LicenseReply>> sender, Func<long> clock, Func<int, Task> pause)
        {
            storageDirectory = directory;
            safeStorage = new GgSafeStorage(directory);
            transport = sender ?? SendHttp;
            now = clock ?? (() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);
            delay = pause ?? (milliseconds => Task.Delay(milliseconds));
        }

        private static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        }

        internal static string NormalizeCode(string value)
        {
            string compact = Regex.Replace((value ?? "").ToUpperInvariant(), "[^A-Z0-9]", "");
            if (!compact.StartsWith("GG", StringComparison.Ordinal) || compact.Length != 14 || compact.Substring(2).Any(character => Alphabet.IndexOf(character) < 0))
                throw new LicenseException("invalid_code", "请输入 GG-XXXX-XXXX-XXXX 格式的卡密。");
            return "GG-" + compact.Substring(2, 4) + "-" + compact.Substring(6, 4) + "-" + compact.Substring(10, 4);
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private void InitializeIdentity()
        {
            if (seed != null) return;
            Directory.CreateDirectory(storageDirectory);
            string filename = Path.Combine(storageDirectory, "device.enc");
            using (Mutex identityLock = new Mutex(false, "Local\\WildfireGG-" + Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(storageDirectory).ToUpperInvariant())).Substring(0, 24)))
            {
                bool acquired = false;
                try
                {
                    try { acquired = identityLock.WaitOne(10000); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new LicenseException("storage_busy", "另一窗口正在读取 GG 授权，请稍后重试。");
                    if (File.Exists(filename))
                    {
                        try
                        {
                            byte[] encrypted = File.ReadAllBytes(filename);
                            if (encrypted.Length <= 3 || Encoding.ASCII.GetString(encrypted, 0, 3) != "v10") throw new InvalidDataException();
                            byte[] plain = safeStorage.Decrypt(encrypted);
                            record = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(plain));
                            byte[] key = Convert.FromBase64String(Text(record, "privateKey"));
                            if (key.Length != 48 || !key.Take(16).SequenceEqual(KeyHeader)) throw new InvalidDataException();
                            seed = key.Skip(16).ToArray();
                        }
                        catch { throw new LicenseException("storage_corrupt", "本机 GG 授权身份无法读取，请联系管理员恢复。原文件已保留，请勿删除或复制其他电脑的授权文件。"); }
                    }
                    else
                    {
                        byte[] freshSeed = new byte[32];
                        using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(freshSeed);
                        record = new Dictionary<string, object> { { "privateKey", Convert.ToBase64String(KeyHeader.Concat(freshSeed).ToArray()) }, { "active", false } };
                        byte[] bytes = EncryptRecord(record);
                        string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, filename); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        seed = freshSeed;
                    }
                    publicKey = Convert.ToBase64String(new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded());
                }
                finally { if (acquired) identityLock.ReleaseMutex(); }
            }
        }

        private byte[] EncryptRecord(Dictionary<string, object> contents)
        {
            byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(contents));
            return safeStorage.Encrypt(plain);
        }

        public LicenseState Snapshot()
        {
            LicenseState current = state.Copy();
            if (current.Valid && current.ExpiresAt.HasValue && (current.ExpiresAt <= now() || (sinceCheck.IsRunning && sinceCheck.ElapsedMilliseconds >= current.ExpiresAt.Value - checkedAt)))
            {
                current.Valid = false; current.Message = "卡密已到期，请续费后重新校验。";
            }
            return current;
        }

        public async Task<LicenseState> VerifyAsync(bool force)
        {
            await gate.WaitAsync();
            try
            {
                LicenseState current = Snapshot();
                if (!force && current.Valid && sinceCheck.IsRunning && sinceCheck.ElapsedMilliseconds < 60000 && now() >= checkedAt) return current;
                InitializeIdentity();
                return Accept(await RequestAsync("verify", new Dictionary<string, object> { { "productId", ProductId } }));
            }
            catch (Exception error) { Reject(error); throw; }
            finally { gate.Release(); }
        }

        public async Task<LicenseState> ActivateAsync(string value, bool replace)
        {
            string code = NormalizeCode(value);
            await gate.WaitAsync();
            try
            {
                InitializeIdentity();
                bool bound = false;
                try
                {
                    LicenseState verified = Accept(await RequestAsync("verify", new Dictionary<string, object> { { "productId", ProductId } }));
                    if (!replace) return verified;
                    bound = true;
                }
                catch (LicenseException error)
                {
                    if (error.Code == "license_expired" || error.Code == "license_revoked" || error.Code == "quota_exhausted")
                    {
                        if (!replace) throw;
                        bound = true;
                    }
                    else if (error.Code != "not_activated") throw;
                }
                Dictionary<string, object> body = new Dictionary<string, object> { { "code", code }, { "productId", ProductId } };
                if (!bound) body["fingerprint"] = Hash(Encoding.UTF8.GetBytes(publicKey));
                LicenseState activated = Accept(await RequestAsync(bound ? "swap" : "activate", body));
                ConfirmSharedActivation();
                return activated;
            }
            catch (Exception error) { Reject(error); throw; }
            finally { gate.Release(); }
        }

        private void Reject(Exception error)
        {
            state = new LicenseState { Message = error is LicenseException ? error.Message : "授权校验未完成，请稍后重试。" };
            sinceCheck.Reset();
        }

        private void ConfirmSharedActivation()
        {
            string filename = Path.Combine(storageDirectory, "device.enc");
            using (Mutex identityLock = new Mutex(false, "Local\\WildfireGG-" + Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(storageDirectory).ToUpperInvariant())).Substring(0, 24)))
            {
                bool acquired = false;
                try
                {
                    try { acquired = identityLock.WaitOne(10000); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new LicenseException("storage_busy", "授权已激活，但本机记录正被其他窗口使用，请稍后重新校验。");
                    byte[] encrypted = File.ReadAllBytes(filename);
                    byte[] plain = safeStorage.Decrypt(encrypted);
                    Dictionary<string, object> current = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(plain));
                    if (Text(current, "privateKey") != Text(record, "privateKey")) throw new LicenseException("storage_corrupt", "本机 GG 身份已发生变化，请重新启动软件校验。");
                    current["active"] = true;
                    string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllBytes(temporary, EncryptRecord(current)); File.Replace(temporary, filename, null); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    record = current;
                }
                finally { if (acquired) identityLock.ReleaseMutex(); }
            }
        }

        private LicenseState Accept(Dictionary<string, object> data)
        {
            if (data == null || Text(data, "productId") != ProductId || Text(data, "status") != "active")
                throw new LicenseException("invalid_license", "授权状态与 GG 产品不匹配，请重新校验。");
            string plan = Text(data, "plan");
            bool lifetime = plan == "lifetime";
            long? expires = Number(data, "expiresAt");
            long timestamp = now();
            if (!lifetime && (!expires.HasValue || expires <= timestamp || expires > 253402300799999L))
                throw new LicenseException("license_expired", expires.HasValue ? "卡密已到期，请续费。" : "这张卡没有授权期限，请在小程序中签发时长体验卡、日卡、月卡或年卡。");
            Dictionary<string, string> labels = new Dictionary<string, string> { { "trial", "体验卡" }, { "hour", "体验卡" }, { "day", "日卡" }, { "month", "月卡" }, { "year", "年卡" }, { "lifetime", "永久卡" } };
            string label = Text(data, "planLabel");
            if (label.Length == 0) label = labels.ContainsKey(plan) ? labels[plan] : "时长卡";
            state = new LicenseState { Valid = true, Plan = plan, Label = label, ExpiresAt = lifetime ? null : expires, Message = "授权有效" };
            checkedAt = timestamp;
            sinceCheck.Restart();
            return Snapshot();
        }

        private async Task<Dictionary<string, object>> RequestAsync(string action, Dictionary<string, object> body)
        {
            byte[] raw = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string timestamp = (now() / 1000).ToString(CultureInfo.InvariantCulture);
                byte[] randomBytes = new byte[24];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(randomBytes);
                string nonce = Convert.ToBase64String(randomBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                byte[] signing = Encoding.UTF8.GetBytes(string.Join("\n", new[] { "POST", "/license/" + action, timestamp, nonce, Hash(raw) }));
                Ed25519Signer signer = new Ed25519Signer();
                signer.Init(true, new Ed25519PrivateKeyParameters(seed, 0)); signer.BlockUpdate(signing, 0, signing.Length);
                Dictionary<string, string> headers = new Dictionary<string, string> {
                    { "x-license-ts", timestamp }, { "x-license-nonce", nonce },
                    { "x-license-signature", Convert.ToBase64String(signer.GenerateSignature()) }, { "x-license-public-key", publicKey }
                };
                LicenseReply reply;
                try { reply = await transport(action, raw, headers); }
                catch (Exception error)
                {
                    if (!(error is HttpRequestException) && !(error is TaskCanceledException) && !(error is WebException)) throw;
                    throw new LicenseException("offline", "无法连接小程序授权服务，请检查网络后重试。");
                }
                Dictionary<string, object> payload = null;
                try { payload = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reply.Body); } catch (ArgumentException) { }
                Dictionary<string, object> details = Child(payload, "error");
                string errorCode = Text(details, "code");
                if (reply.Status == 429 || Text(payload, "error_type") == "rate_limited" || errorCode == "rate_limited")
                {
                    if (attempt < 2) { await delay(Math.Min(8000, reply.RetryMilliseconds > 0 ? reply.RetryMilliseconds : 1000 * (1 << attempt))); continue; }
                    throw new LicenseException("rate_limited", "授权服务繁忙，已自动重试两次，请稍后重试。");
                }
                object ok;
                if (reply.Status < 200 || reply.Status >= 300 || payload == null || !payload.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok)
                    throw new LicenseException(errorCode.Length == 0 ? "service_error" : errorCode, Friendly(errorCode));
                return Child(payload, "data");
            }
            throw new LicenseException("service_error", "授权服务暂不可用，请稍后重试。");
        }

        private static async Task<LicenseReply> SendHttp(string action, byte[] body, Dictionary<string, string> headers)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ApiBase + "/" + action))
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                foreach (KeyValuePair<string, string> header in headers) request.Headers.Add(header.Key, header.Value);
                using (HttpResponseMessage response = await Http.SendAsync(request))
                {
                    int retry = response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue ? (int)Math.Min(8000, response.Headers.RetryAfter.Delta.Value.TotalMilliseconds) : 0;
                    return new LicenseReply { Status = (int)response.StatusCode, Body = await response.Content.ReadAsStringAsync(), RetryMilliseconds = retry };
                }
            }
        }

        internal static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        }

        private static long? Number(Dictionary<string, object> data, string key)
        {
            object value;
            long parsed;
            return data != null && data.TryGetValue(key, out value) && value != null && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? (long?)parsed : null;
        }

        private static Dictionary<string, object> Child(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string Friendly(string code)
        {
            Dictionary<string, string> messages = new Dictionary<string, string> {
                { "not_activated", "本机尚未绑定 GG 卡密，请输入卡密激活。" }, { "license_not_found", "卡密不存在，请检查输入。" },
                { "invalid_code", "卡密格式不正确，请输入 GG-XXXX-XXXX-XXXX。" }, { "license_wrong_product", "这张卡不属于 GG 产品，请使用 yuansheng 卡密。" },
                { "license_expired", "卡密已到期，请续费后使用“更换卡密”。" }, { "license_revoked", "卡密已被停用，请联系管理员。" },
                { "device_limit", "已达设备绑定上限，请联系管理员解绑旧设备。" }, { "machine_mismatch", "卡密绑定设备不匹配，请联系管理员。" },
                { "device_bound_elsewhere", "本机已绑定其他 GG 卡密，请使用“更换卡密”。" }, { "quota_exhausted", "卡密权益已用完，请联系管理员。" },
                { "stale_timestamp", "电脑时间不准确，请校准时间后重试。" }, { "swap_cooldown", "更换卡密处于冷却时间，请稍后重试。" },
                { "unknown_product", "云端未配置 GG 产品，请联系管理员。" }, { "bad_signature", "授权通信校验失败，请重试或联系管理员。" }
            };
            return messages.ContainsKey(code) ? messages[code] : "授权校验失败，请稍后重试或联系管理员。";
        }
    }
}
