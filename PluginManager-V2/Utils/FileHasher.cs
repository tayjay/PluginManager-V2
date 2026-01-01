using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PluginManager_V2.Utils;

public class FileHasher
{
    public static string GetFileHashSha256(string filename)
    {
        // Use 'using' statements to ensure proper disposal of resources
        using (var sha256 = SHA256.Create())
        using (var stream = File.OpenRead(filename))
        {
            // Compute the hash of the stream
            byte[] hashBytes = sha256.ComputeHash(stream);

            // Convert the byte array to a hexadecimal string
            var sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2")); // "x2" formats as a two-digit hexadecimal
            }
            return sb.ToString();
        }
    }
}