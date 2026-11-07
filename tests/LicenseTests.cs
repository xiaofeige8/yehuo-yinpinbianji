using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace WildfireAudioEditor
{
    internal static class LicenseTests
    {
        private static int passed;
        private const string FakeCode = "GG-2345-6789-ABCD";
        private static long time = 1800000000000L;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly List<object> captures = new List<object>();

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Check(string name, Action run)
        {
            run(); passed++; Console.WriteLine("PASS " + name);
        }

        private static void Error(string expected, Action run)
        {
            try { run(); } catch (LicenseException error) { Require(error.Code == expected, "Expected " + expected + ", received " + error.Code); return; }
            throw new Exception("Expected rejection: " + expected);
        }

        private static LicenseReply Success(string plan, long? expiry, string product = OnlineLicense.ProductId, string status = "active")
        {
            return new LicenseReply { Status = 200, Body = Json.Serialize(new { ok = true, data = new { productId = product, status, plan, expiresAt = expiry, quota = new { unlimited = true } } }) };
        }

        private static LicenseReply Failure(string code)
        {
            return new LicenseReply { Status = 403, Body = Json.Serialize(new { ok = false, error = new { code, message = "server message" } }) };
        }

        private static OnlineLicense Client(string folder, Func<string, LicenseReply> reply)
        {
            return new OnlineLicense(folder, (action, body, headers) =>
            {
                string raw = Encoding.UTF8.GetString(body);
                Dictionary<string, object> request = Json.Deserialize<Dictionary<string, object>>(raw);
                Require(Convert.ToString(request["productId"]) == OnlineLicense.ProductId, "wrong product");
                Require(!raw.Contains("privateKey") && !raw.Contains("seed"), "private key must not be sent");
                byte[] message = Encoding.UTF8.GetBytes(string.Join("\n", new[] { "POST", "/license/" + action, headers["x-license-ts"], headers["x-license-nonce"], OnlineLicense.Hash(body) }));
                Ed25519Signer verifier = new Ed25519Signer();
                verifier.Init(false, new Ed25519PublicKeyParameters(Convert.FromBase64String(headers["x-license-public-key"]), 0));
                verifier.BlockUpdate(message, 0, message.Length);
                Require(verifier.VerifySignature(Convert.FromBase64String(headers["x-license-signature"])), "request signature invalid");
                captures.Add(new { action, raw, headers = new Dictionary<string, string>(headers) });
                return Task.FromResult(reply(action));
            }, () => time, milliseconds => Task.FromResult(0));
        }

        private static byte[] DecodeRecord(string folder)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "device.enc"));
            Require(Encoding.ASCII.GetString(bytes, 0, 3) == "v10", "must match Electron Windows safeStorage header");
            return new GgSafeStorage(folder).Decrypt(bytes);
        }

        private static void Run(string root)
        {
            Check("GG formatting and wrong prefixes", () =>
            {
                Require(OnlineLicense.NormalizeCode("gg 2345 6789 abcd") == FakeCode, "normalize failed");
                foreach (string wrong in new[] { "YL-2345-6789-ABCD", "GG-OOOO-1111-ABCD", "GG-2345-6789-ABCDE" }) Error("invalid_code", () => OnlineLicense.NormalizeCode(wrong));
            });
            foreach (string plan in new[] { "trial", "day", "month", "year", "lifetime" })
            {
                Check(plan + " accepted with server expiry", () =>
                {
                    string folder = Path.Combine(root, plan);
                    OnlineLicense client = Client(folder, action => Success(plan, plan == "lifetime" ? (long?)null : time + 86400000));
                    LicenseState state = client.VerifyAsync(true).GetAwaiter().GetResult();
                    Require(state.Valid && state.Plan == plan, "plan rejected");
                    Require(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "device.enc"))).Contains("privateKey"), "identity not protected");
                });
            }
            Check("existing GG identity reused after reinstall without rewriting", () =>
            {
                string folder = Path.Combine(root, "month");
                byte[] before = File.ReadAllBytes(Path.Combine(folder, "device.enc"));
                string oldKey = Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(DecodeRecord(folder)))["privateKey"].ToString();
                OnlineLicense client = Client(folder, action => Success("month", time + 86400000));
                client.VerifyAsync(true).GetAwaiter().GetResult();
                Require(before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "device.enc"))), "verify rewrote identity");
                Require(oldKey == Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(DecodeRecord(folder)))["privateKey"].ToString(), "identity changed");
            });
            Check("entering same card on active device only verifies", () =>
            {
                List<string> calls = new List<string>();
                OnlineLicense client = Client(Path.Combine(root, "repeat"), action => { calls.Add(action); return Success("year", time + 86400000); });
                Require(client.ActivateAsync(FakeCode, false).GetAwaiter().GetResult().Valid, "verify failed");
                Require(calls.SequenceEqual(new[] { "verify" }), "repeated activation sent");
            });
            Check("unbound device activates once and shares active confirmation", () =>
            {
                string folder = Path.Combine(root, "activate");
                List<string> calls = new List<string>();
                OnlineLicense client = Client(folder, action => { calls.Add(action); return action == "verify" ? Failure("not_activated") : Success("trial", time + 86400000); });
                Require(client.ActivateAsync(FakeCode, false).GetAwaiter().GetResult().Valid, "activation failed");
                Require(calls.SequenceEqual(new[] { "verify", "activate" }), "wrong activation sequence");
                Require((bool)Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(DecodeRecord(folder)))["active"], "shared GG confirmation missing");
            });
            Check("explicit replacement uses swap and retains identity", () =>
            {
                List<string> calls = new List<string>();
                string folder = Path.Combine(root, "replace");
                OnlineLicense client = Client(folder, action => { calls.Add(action); return Success("year", time + 86400000); });
                client.VerifyAsync(true).GetAwaiter().GetResult();
                byte[] seedRecord = DecodeRecord(folder);
                Require(client.ActivateAsync(FakeCode, true).GetAwaiter().GetResult().Valid, "replacement failed");
                Require(calls.SequenceEqual(new[] { "verify", "verify", "swap" }), "wrong replacement sequence");
                Require(OnlineLicense.Text(Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(seedRecord)), "privateKey") == OnlineLicense.Text(Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(DecodeRecord(folder))), "privateKey"), "swap changed identity");
            });
            Check("expired cards do not silently reactivate", () =>
            {
                List<string> calls = new List<string>();
                OnlineLicense client = Client(Path.Combine(root, "expired-activation"), action => { calls.Add(action); return Failure("license_expired"); });
                Error("license_expired", () => client.ActivateAsync(FakeCode, false).GetAwaiter().GetResult());
                Require(calls.SequenceEqual(new[] { "verify" }), "expired card activated");
            });
            Check("wrong product, inactive status, expired cards and untimed trial rejected", () =>
            {
                foreach (LicenseReply reply in new[] { Success("month", time + 1000, "yinli-studio"), Success("month", time + 1000, OnlineLicense.ProductId, "revoked"), Success("day", time - 1), Success("trial", null) })
                {
                    OnlineLicense client = Client(Path.Combine(root, Guid.NewGuid().ToString("N")), action => reply);
                    bool rejected = false;
                    try { client.VerifyAsync(true).GetAwaiter().GetResult(); } catch (LicenseException) { rejected = true; }
                    Require(rejected && !client.Snapshot().Valid, "invalid card allowed");
                }
            });
            Check("expiration invalidates cached authorization", () =>
            {
                OnlineLicense client = Client(Path.Combine(root, "expiry"), action => Success("day", time + 1000));
                client.VerifyAsync(true).GetAwaiter().GetResult(); time += 1001;
                Require(!client.Snapshot().Valid, "expired cache still valid");
            });
            Check("service revocation immediately removes prior permission", () =>
            {
                bool revoked = false;
                OnlineLicense client = Client(Path.Combine(root, "revoked"), action => revoked ? Failure("license_revoked") : Success("lifetime", null));
                client.VerifyAsync(true).GetAwaiter().GetResult(); revoked = true;
                Error("license_revoked", () => client.VerifyAsync(true).GetAwaiter().GetResult());
                Require(!client.Snapshot().Valid, "revoked card retained");
            });
            Check("gateway rate limit retried twice with fresh signed nonces", () =>
            {
                int requests = 0;
                int before = captures.Count;
                OnlineLicense client = Client(Path.Combine(root, "retry"), action => ++requests < 3 ? new LicenseReply { Status = 429, Body = "{\"error_type\":\"rate_limited\"}" } : Success("month", time + 1000));
                Require(client.VerifyAsync(true).GetAwaiter().GetResult().Valid && requests == 3, "retry failed");
                HashSet<string> nonces = new HashSet<string>();
                foreach (object capture in captures.Skip(before))
                {
                    Dictionary<string, object> contents = Json.Deserialize<Dictionary<string, object>>(Json.Serialize(capture));
                    nonces.Add(Convert.ToString(((Dictionary<string, object>)contents["headers"])["x-license-nonce"]));
                }
                Require(nonces.Count == 3, "nonce reused");
            });
            Check("third rate limit stops with no activation or permission", () =>
            {
                int requests = 0;
                OnlineLicense client = Client(Path.Combine(root, "retry-exhausted"), action => { requests++; return new LicenseReply { Status = 429, Body = "{}" }; });
                Error("rate_limited", () => client.ActivateAsync(FakeCode, false).GetAwaiter().GetResult());
                Require(requests == 3 && !client.Snapshot().Valid, "retry cap not enforced");
            });
            Check("network failure does not trigger binding", () =>
            {
                int requests = 0;
                OnlineLicense client = new OnlineLicense(Path.Combine(root, "offline"), (action, body, headers) => { requests++; throw new HttpRequestException(); }, () => time, null);
                Error("offline", () => client.ActivateAsync(FakeCode, false).GetAwaiter().GetResult());
                Require(requests == 1 && !client.Snapshot().Valid, "offline triggered activation");
            });
            Check("corrupt identity preserved and no request sent", () =>
            {
                string folder = Path.Combine(root, "corrupt"); Directory.CreateDirectory(folder);
                byte[] corrupt = Encoding.UTF8.GetBytes("not a device"); File.WriteAllBytes(Path.Combine(folder, "device.enc"), corrupt);
                int requests = 0;
                OnlineLicense client = Client(folder, action => { requests++; return Success("lifetime", null); });
                Error("storage_corrupt", () => client.VerifyAsync(true).GetAwaiter().GetResult());
                Require(requests == 0 && corrupt.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "device.enc"))), "corrupt identity replaced");
            });
        }

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--real-verify")
                {
                    OnlineLicense client = new OnlineLicense();
                    LicenseState current = client.VerifyAsync(true).GetAwaiter().GetResult();
                    Console.WriteLine(Json.Serialize(new { valid = current.Valid, plan = current.Plan, expiresAt = current.ExpiresAt, productId = OnlineLicense.ProductId }));
                    return current.Valid ? 0 : 1;
                }
                string root = Path.Combine(Path.GetTempPath(), "wildfire-gg-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try { Run(root); }
                finally { Directory.Delete(root, true); }
                if (args.Length > 0) File.WriteAllText(args[0], Json.Serialize(captures), new UTF8Encoding(false));
                Console.WriteLine("Passed: " + passed);
                return 0;
            }
            catch (LicenseException error) { Console.WriteLine("FAIL " + error.Code + ": " + error.Message); return 1; }
            catch (Exception error) { Console.WriteLine(error); return 1; }
        }
    }
}
