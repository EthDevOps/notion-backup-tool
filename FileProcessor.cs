using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace NotionBackupTool;

public class FileProcessor
{
    public void CreateTarGz(string outputPath, string inputDirectory)
    {
        using Stream targetStream = File.OpenWrite(outputPath);
        using var writer = WriterFactory.Open(targetStream, ArchiveType.Tar, CompressionType.GZip);
        writer.WriteAll(inputDirectory, searchPattern: "*", SearchOption.AllDirectories);
    }

    public static void DecryptFileSymetric(string inputFile, string outputFile, string password)
    {
        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        byte[] salt = new byte[32];

        FileStream fsCrypt = new FileStream(inputFile, FileMode.Open);
        fsCrypt.Read(salt, 0, salt.Length);
    
        Rfc2898DeriveBytes derivedBytes = new Rfc2898DeriveBytes(passwordBytes, salt, 50000, HashAlgorithmName.SHA256);
        Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Key = derivedBytes.GetBytes(aes.KeySize / 8);
        aes.IV = derivedBytes.GetBytes(aes.BlockSize / 8);

        CryptoStream cs = new CryptoStream(fsCrypt, aes.CreateDecryptor(), CryptoStreamMode.Read);
        FileStream fsOut = new FileStream(outputFile, FileMode.Create);

        int read;
        byte[] buffer = new byte[1048576];

        try
        {
            while ((read = cs.Read(buffer, 0, buffer.Length)) > 0)
            {
                fsOut.Write(buffer, 0, read);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }

        try
        {
            cs.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error by closing CryptoStream: " + ex.Message);
        }
        finally
        {
            fsOut.Close();
            fsCrypt.Close();
        }
    }

    
    public void EncryptFileSymetric(string inputFile, string outputFile, string pgpKeyFile)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        string randomPassword = RandomNumberGenerator.GetHexString(128);
        
        // Encrypt password with Pgp
        Console.WriteLine($"Password used: {randomPassword}");
        
        EncryptStringPgp(randomPassword, pgpKeyFile, $"{outputFile}.key"); 
        
        FileStream fsCrypt = new FileStream(outputFile, FileMode.Create);

        byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(randomPassword);
        Rfc2898DeriveBytes derivedBytes = new Rfc2898DeriveBytes(passwordBytes, salt, 50000, HashAlgorithmName.SHA256);
        Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Key = derivedBytes.GetBytes(aes.KeySize / 8);
        aes.IV = derivedBytes.GetBytes(aes.BlockSize / 8);

        fsCrypt.Write(salt, 0, salt.Length);

        CryptoStream cs = new CryptoStream(fsCrypt, aes.CreateEncryptor(), CryptoStreamMode.Write);
        FileStream fsIn = new FileStream(inputFile, FileMode.Open);

        byte[] buffer = new byte[1048576];

        Console.Write("Encrypting file.");
        try
        {
            int read;
            while ((read = fsIn.Read(buffer, 0, buffer.Length)) > 0)
            {
                cs.Write(buffer, 0, read);
                Console.Write("."); 
            }

            fsIn.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
        finally
        {
            cs.Close();
            fsCrypt.Close();
        }

        Console.WriteLine("done");
    }

    
    public void EncryptFilePGP(string inputFile, string publicKeyFile, string outputFile, bool armor, bool withIntegrityCheck)
    {
        using Stream publicKeyStream = File.OpenRead(publicKeyFile);
        PgpPublicKey encKey = ReadPublicKey(publicKeyStream);

        using MemoryStream bOut = new MemoryStream();
        PgpCompressedDataGenerator comData = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);

        PgpUtilities.WriteFileToLiteralData(
            comData.Open(bOut),
            PgpLiteralData.Binary,
            new FileInfo(inputFile));

        comData.Close();

        PgpEncryptedDataGenerator cPk = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Cast5, withIntegrityCheck, new SecureRandom());

        cPk.AddMethod(encKey);

        byte[] bytes = bOut.ToArray();

        using Stream outputStream = File.Create(outputFile);
        if (armor)
        {
            using Stream armoredStream = new ArmoredOutputStream(outputStream);
            WriteBytesToStream(cPk.Open(armoredStream, bytes.Length), bytes);
        }
        else
        {
            WriteBytesToStream(cPk.Open(outputStream, bytes.Length), bytes);
        }
    }

    public void EncryptStringPgp(string inputString, string publicKeyFile, string outputFile)
    {
        using Stream publicKeyStream = File.OpenRead(publicKeyFile);
        PgpPublicKey encKey = ReadPublicKey(publicKeyStream);

        PgpEncryptedDataGenerator cPk = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithmTag.Cast5, true, new SecureRandom());
        cPk.AddMethod(encKey);

        byte[] bytes = Encoding.UTF8.GetBytes(inputString);

        using Stream outputStream = File.Create(outputFile);
        using Stream armoredStream = new ArmoredOutputStream(outputStream);
        WriteBytesToStream(cPk.Open(armoredStream, bytes.Length), bytes);
    }
    private static void WriteBytesToStream(Stream outputStream, byte[] bytes)
    {
        using Stream encryptedOut = outputStream;
        encryptedOut.Write(bytes, 0, bytes.Length);
    }
    
    private static PgpPublicKey ReadPublicKey(Stream inputStream)
    {
        using Stream keyIn = inputStream;
        PgpPublicKeyRingBundle pgpPub = new PgpPublicKeyRingBundle(PgpUtilities.GetDecoderStream(keyIn));

        foreach (PgpPublicKeyRing keyRing in pgpPub.GetKeyRings())
        {
            foreach (PgpPublicKey key in keyRing.GetPublicKeys())
            {
                if (key.IsEncryptionKey)
                {
                    return key;
                }
            }
        }

        throw new ArgumentException("Can't find encryption key in key ring.");
    }

}