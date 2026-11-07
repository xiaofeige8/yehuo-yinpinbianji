using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace WildfireAudioEditor
{
    internal sealed class GgSafeStorage
    {
        private readonly string stateFile;
        private byte[] key;

        internal GgSafeStorage(string identityDirectory)
        {
            stateFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(identityDirectory)), "YSGF7-GG-UI", "Local State");
        }

        private byte[] LoadKey(bool create)
        {
            if (key != null) return key;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> state = File.Exists(stateFile) ? serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(stateFile, Encoding.UTF8)) : new Dictionary<string, object>();
            object value;
            Dictionary<string, object> crypt = state.TryGetValue("os_crypt", out value) ? value as Dictionary<string, object> : null;
            if (crypt != null && crypt.TryGetValue("encrypted_key", out value))
            {
                byte[] wrapped = Convert.FromBase64String(Convert.ToString(value));
                if (wrapped.Length <= 5 || Encoding.ASCII.GetString(wrapped, 0, 5) != "DPAPI") throw new InvalidDataException("Unsupported GG key format");
                byte[] loaded = ProtectedData.Unprotect(wrapped.Skip(5).ToArray(), null, DataProtectionScope.CurrentUser);
                if (loaded.Length != 32) throw new InvalidDataException("Invalid GG key");
                key = loaded;
            }
            else
            {
                if (!create) throw new InvalidDataException("Missing GG safeStorage key");
                byte[] fresh = new byte[32];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(fresh);
                if (crypt == null) crypt = new Dictionary<string, object>();
                crypt["encrypted_key"] = Convert.ToBase64String(Encoding.ASCII.GetBytes("DPAPI").Concat(ProtectedData.Protect(fresh, null, DataProtectionScope.CurrentUser)).ToArray());
                state["os_crypt"] = crypt;
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile));
                string temporary = stateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, serializer.Serialize(state), new UTF8Encoding(false));
                    if (File.Exists(stateFile)) File.Replace(temporary, stateFile, null);
                    else File.Move(temporary, stateFile);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                key = fresh;
            }
            return key;
        }

        internal byte[] Encrypt(byte[] plain)
        {
            byte[] nonce = new byte[12];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
            GcmBlockCipher cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(true, new AeadParameters(new KeyParameter(LoadKey(true)), 128, nonce));
            byte[] encrypted = new byte[cipher.GetOutputSize(plain.Length)];
            int written = cipher.ProcessBytes(plain, 0, plain.Length, encrypted, 0);
            cipher.DoFinal(encrypted, written);
            return Encoding.ASCII.GetBytes("v10").Concat(nonce).Concat(encrypted).ToArray();
        }

        internal byte[] Decrypt(byte[] encrypted)
        {
            if (encrypted.Length < 31 || Encoding.ASCII.GetString(encrypted, 0, 3) != "v10") throw new InvalidDataException("Invalid GG record");
            byte[] nonce = encrypted.Skip(3).Take(12).ToArray();
            byte[] contents = encrypted.Skip(15).ToArray();
            GcmBlockCipher cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(false, new AeadParameters(new KeyParameter(LoadKey(false)), 128, nonce));
            byte[] plain = new byte[cipher.GetOutputSize(contents.Length)];
            int written = cipher.ProcessBytes(contents, 0, contents.Length, plain, 0);
            cipher.DoFinal(plain, written);
            return plain;
        }
    }
}
