using Org.BouncyCastle.Bcpg;
using PgpCore;
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

    public async Task EncryptFilePgp(string inputFile, string publicKeyFile, string outputFile)
    {
        // Load key
        FileInfo publicKey = new FileInfo(publicKeyFile);
        EncryptionKeys encryptionKeys = new EncryptionKeys(publicKey);

        // Get file infos
        FileInfo inputFileInfo = new FileInfo(inputFile);
        FileInfo encryptedSignedFile = new FileInfo(outputFile); 
        // Encrypt and Sign
        PGP pgp = new PGP(encryptionKeys);
        pgp.SymmetricKeyAlgorithm = SymmetricKeyAlgorithmTag.Aes256;
        await pgp.EncryptFileAsync(inputFileInfo, encryptedSignedFile);
    }

    public async Task DecryptFilePgp(string inputFilePath, string outputFilePath, string privateKeyPath, string password)
    {
        // Load keys
        EncryptionKeys encryptionKeys;
        await using (Stream privateKeyStream = new FileStream(privateKeyPath, FileMode.Open))
            encryptionKeys = new EncryptionKeys(privateKeyStream, password);
        
        // Get file infos
        FileInfo inputFileInfo = new FileInfo(inputFilePath);
        FileInfo outputFileInfo = new FileInfo(outputFilePath); 

        PGP pgp = new PGP(encryptionKeys);
        pgp.SymmetricKeyAlgorithm = SymmetricKeyAlgorithmTag.Aes256;

        // Reference input/output files
        await pgp.DecryptFileAsync(inputFileInfo, outputFileInfo);
    }
}