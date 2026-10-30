namespace Test.Shared.Infrastructure.BlackBoxStorage
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using OpenCIFS.Server;
    using OpenNFS.Server;
    using OpenNFS.Server.FileHandles;

    /// <summary>
    /// Live black box storage targets for the contract suites, started once per test process and torn down at exit:
    /// Disk (a temporary directory), S3 (a throwaway MinIO container), AzureBlob (Azurite), GoogleCloud
    /// (fake-gcs-server), Cifs (an in-process OpenCIFS server) and Nfs (an in-process OpenNFS server). Docker-backed
    /// targets report a skip reason when Docker or the image is unavailable, so their suites skip instead of failing.
    /// Set ARMADA_TEST_NO_DOCKER=1 to skip them on purpose; override images with ARMADA_TEST_MINIO_IMAGE,
    /// ARMADA_TEST_AZURITE_IMAGE and ARMADA_TEST_FAKEGCS_IMAGE.
    /// </summary>
    public static class BlackBoxStorageEmulators
    {
        #region Private-Members

        private const string Label = "armada.test=blackbox-storage";
        private const string MinioUser = "armadaminio";
        private const string MinioPassword = "armada-minio-secret";
        private const string AzuriteAccount = "devstoreaccount1";
        private const string AzuriteKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
        private const string CifsUser = "armada";
        private const string CifsPassword = "Armada-Test-123!";
        private const string Bucket = "armada-blackbox";

        private static readonly string[] _MinioImages = { "minio/minio:latest", "pgsty/minio:RELEASE.2026-08-04T00-00-00Z", "quay.io/minio/minio:latest" };
        private static readonly string[] _AzuriteImages = { "mcr.microsoft.com/azure-storage/azurite:3.35.0", "mcr.microsoft.com/azure-storage/azurite:latest" };
        private static readonly string[] _FakeGcsImages = { "fsouza/fake-gcs-server:1.56.1", "fsouza/fake-gcs-server:latest" };

        private static readonly Lazy<string?> _DockerProblem = new Lazy<string?>(ProbeDocker, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly ConcurrentDictionary<BlackBoxStorageTargetEnum, Lazy<Task<BlackBoxTargetEnvironment>>> _Started = new ConcurrentDictionary<BlackBoxStorageTargetEnum, Lazy<Task<BlackBoxTargetEnvironment>>>();
        private static readonly ConcurrentBag<string> _Containers = new ConcurrentBag<string>();
        private static readonly ConcurrentBag<IAsyncDisposable> _Servers = new ConcurrentBag<IAsyncDisposable>();
        private static readonly ConcurrentBag<string> _Directories = new ConcurrentBag<string>();
        private static int _ExitHooked = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Why a target cannot run here (Docker or its image missing), or null when it can. Cheap after the first call.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <returns>Skip reason, or null.</returns>
        public static string? SkipReason(BlackBoxStorageTargetEnum target)
        {
            switch (target)
            {
                case BlackBoxStorageTargetEnum.S3:
                    return DockerImageProblem(_MinioImages, "ARMADA_TEST_MINIO_IMAGE", "MinIO");
                case BlackBoxStorageTargetEnum.AzureBlob:
                    return DockerImageProblem(_AzuriteImages, "ARMADA_TEST_AZURITE_IMAGE", "Azurite");
                case BlackBoxStorageTargetEnum.GoogleCloud:
                    return DockerImageProblem(_FakeGcsImages, "ARMADA_TEST_FAKEGCS_IMAGE", "fake-gcs-server");
                default:
                    return null;
            }
        }

        /// <summary>
        /// The live environment of a target, starting it on first use.
        /// </summary>
        /// <param name="target">Target (not Database).</param>
        /// <returns>The environment.</returns>
        public static Task<BlackBoxTargetEnvironment> GetAsync(BlackBoxStorageTargetEnum target)
        {
            HookExit();
            return _Started.GetOrAdd(target, t => new Lazy<Task<BlackBoxTargetEnvironment>>(() => StartAsync(t), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        #endregion

        #region Private-Methods

        private static async Task<BlackBoxTargetEnvironment> StartAsync(BlackBoxStorageTargetEnum target)
        {
            switch (target)
            {
                case BlackBoxStorageTargetEnum.Disk:
                    string disk = Track(TestTemp.NewDirectory("bbdisk"));
                    return new BlackBoxTargetEnvironment(target, "temporary directory " + disk, s => s.Disk.Directory = disk);
                case BlackBoxStorageTargetEnum.S3:
                    return await StartMinioAsync().ConfigureAwait(false);
                case BlackBoxStorageTargetEnum.AzureBlob:
                    return await StartAzuriteAsync().ConfigureAwait(false);
                case BlackBoxStorageTargetEnum.GoogleCloud:
                    return await StartFakeGcsAsync().ConfigureAwait(false);
                case BlackBoxStorageTargetEnum.Cifs:
                    return await StartCifsAsync().ConfigureAwait(false);
                case BlackBoxStorageTargetEnum.Nfs:
                    return await StartNfsAsync().ConfigureAwait(false);
                default:
                    throw new NotSupportedException("No test environment for " + target + ".");
            }
        }

        private static async Task<BlackBoxTargetEnvironment> StartMinioAsync()
        {
            string image = PickImage(_MinioImages, "ARMADA_TEST_MINIO_IMAGE")!;
            int port = TestPorts.Reserve(1)[0];
            RunContainer("-p 127.0.0.1:" + port + ":9000 -e MINIO_ROOT_USER=" + MinioUser + " -e MINIO_ROOT_PASSWORD=" + MinioPassword + " " + image + " server /data");
            string endpoint = "http://127.0.0.1:" + port;
            await WaitHttpAsync(endpoint + "/minio/health/live", TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            Amazon.S3.AmazonS3Config config = new Amazon.S3.AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true, UseHttp = true, AuthenticationRegion = "us-east-1" };
            using (Amazon.S3.AmazonS3Client client = new Amazon.S3.AmazonS3Client(MinioUser, MinioPassword, config))
            {
                await RetryAsync(() => client.PutBucketAsync(Bucket)).ConfigureAwait(false);
            }
            return new BlackBoxTargetEnvironment(BlackBoxStorageTargetEnum.S3, "MinIO " + image + " on " + endpoint, s =>
            {
                s.S3.Endpoint = endpoint;
                s.S3.UseSsl = false;
                s.S3.UsePathStyle = true;
                s.S3.Region = "us-east-1";
                s.S3.Bucket = Bucket;
                s.S3.AccessKey = MinioUser;
                s.S3.SecretKey = MinioPassword;
            });
        }

        private static async Task<BlackBoxTargetEnvironment> StartAzuriteAsync()
        {
            string image = PickImage(_AzuriteImages, "ARMADA_TEST_AZURITE_IMAGE")!;
            int port = TestPorts.Reserve(1)[0];
            RunContainer("-p 127.0.0.1:" + port + ":10000 " + image + " azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck --loose");
            await WaitTcpAsync(port, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            string endpoint = "http://127.0.0.1:" + port + "/" + AzuriteAccount + "/";
            Azure.Storage.Blobs.BlobServiceClient service = new Azure.Storage.Blobs.BlobServiceClient("DefaultEndpointsProtocol=http;AccountName=" + AzuriteAccount + ";AccountKey=" + AzuriteKey + ";BlobEndpoint=http://127.0.0.1:" + port + "/" + AzuriteAccount + ";");
            await RetryAsync(() => service.GetBlobContainerClient(Bucket).CreateIfNotExistsAsync()).ConfigureAwait(false);
            return new BlackBoxTargetEnvironment(BlackBoxStorageTargetEnum.AzureBlob, "Azurite " + image + " on " + endpoint, s =>
            {
                s.AzureBlob.AccountName = AzuriteAccount;
                s.AzureBlob.AccessKey = AzuriteKey;
                s.AzureBlob.Endpoint = endpoint;
                s.AzureBlob.Container = Bucket;
            });
        }

        private static async Task<BlackBoxTargetEnvironment> StartFakeGcsAsync()
        {
            string image = PickImage(_FakeGcsImages, "ARMADA_TEST_FAKEGCS_IMAGE")!;
            int port = TestPorts.Reserve(1)[0];
            RunContainer("-p 127.0.0.1:" + port + ":4443 " + image + " -scheme http -port 4443 -public-host 127.0.0.1:" + port);
            string root = "http://127.0.0.1:" + port;
            await WaitHttpAsync(root + "/storage/v1/b", TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            using (HttpClient http = new HttpClient())
            {
                await RetryAsync(async () =>
                {
                    HttpResponseMessage response = await http.PostAsync(root + "/storage/v1/b?project=armada-test", new StringContent("{\"name\":\"" + Bucket + "\"}", Encoding.UTF8, "application/json")).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict) throw new InvalidOperationException("bucket: " + response.StatusCode);
                }).ConfigureAwait(false);
            }
            string credentials = Track(TestTemp.NewFile("bbgcs", ".json"));
            using (RSA rsa = RSA.Create(2048))
            {
                string key = rsa.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n");
                File.WriteAllText(credentials, "{\"type\":\"service_account\",\"project_id\":\"armada-test\",\"private_key_id\":\"test\",\"private_key\":\"" + key
                    + "\",\"client_email\":\"armada@armada-test.iam.gserviceaccount.com\",\"client_id\":\"1\",\"token_uri\":\"" + root + "/token\"}");
            }
            return new BlackBoxTargetEnvironment(BlackBoxStorageTargetEnum.GoogleCloud, "fake-gcs-server " + image + " on " + root, s =>
            {
                s.GoogleCloud.ProjectId = "armada-test";
                s.GoogleCloud.Bucket = Bucket;
                s.GoogleCloud.CredentialsPath = credentials;
                s.GoogleCloud.Endpoint = root + "/storage/v1/";
            }, enumerationReliable: false);
        }

        private static async Task<BlackBoxTargetEnvironment> StartCifsAsync()
        {
            string root = Track(TestTemp.NewDirectory("bbcifs"));
            int port = TestPorts.Reserve(1)[0];
            OpenCifsServerApplication application = new OpenCifsServerBuilder()
                .WithServerName("armada-test")
                .WithBindAddress("127.0.0.1")
                .WithBindPort(port)
                .AddAccount(new OpenCifsServerAccount { UserName = CifsUser, UserDomain = "WORKGROUP", Password = CifsPassword })
                .AddSrvsvcShareEnumerationEndpoint()
                .AddFileSystemShare("share", root, true)
                .BuildApplication(e => { });
            await application.StartAsync(CancellationToken.None).ConfigureAwait(false);
            _Servers.Add(application);
            await WaitTcpAsync(port, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return new BlackBoxTargetEnvironment(BlackBoxStorageTargetEnum.Cifs, "OpenCIFS.Server (in-process) on 127.0.0.1:" + port, s =>
            {
                s.Cifs.Hostname = "127.0.0.1";
                s.Cifs.Port = port;
                s.Cifs.Share = "share";
                s.Cifs.Domain = "WORKGROUP";
                s.Cifs.Username = CifsUser;
                s.Cifs.Password = CifsPassword;
                // OpenCIFS negotiates at most SMB 3.0.2, whose only cipher is AES-128-CCM; platforms without it (macOS) cannot encrypt.
                s.Cifs.PreferEncryption = AesCcm.IsSupported;
            });
        }

        private static async Task<BlackBoxTargetEnvironment> StartNfsAsync()
        {
            string baseDirectory = Track(TestTemp.NewDirectory("bbnfs"));
            string root = Path.Combine(baseDirectory, "export");
            string state = Path.Combine(baseDirectory, "state");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(state);
            // The default handle provider embeds the full path in the handle, which exceeds the NFSv3 64-byte limit for temp paths.
            OpenNfsServerApplication application = new OpenNfsServerBuilder()
                .WithServerName("armada-test")
                .UseLocalFileSystem()
                .UseFileHandleProvider(new PersistentMappingHandleProvider(Path.Combine(state, "handles.json")))
                .AddExport("/export", root, false)
                .BuildApplication(new OpenNfsServerApplicationOptions
                {
                    ListenerAddress = "127.0.0.1",
                    EnableNfsV3 = true,
                    EnableNfs40 = false,
                    EnableNfs41 = false,
                    EnableNfs42 = false,
                    NfsPort = 0,
                    MountPort = 0
                });
            await application.StartAsync(CancellationToken.None).ConfigureAwait(false);
            _Servers.Add(application);
            int nfsPort = application.NfsPort;
            int mountPort = application.MountPort;
            await WaitTcpAsync(nfsPort, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            return new BlackBoxTargetEnvironment(BlackBoxStorageTargetEnum.Nfs, "OpenNFS.Server (in-process, NFSv3) on 127.0.0.1:" + nfsPort + " (mount " + mountPort + ")", s =>
            {
                s.Nfs.Hostname = "127.0.0.1";
                s.Nfs.ExportPath = "/export";
                s.Nfs.Version = 3;
                s.Nfs.Port = nfsPort;
                s.Nfs.MountPort = mountPort;
            });
        }

        private static string? ProbeDocker()
        {
            if (String.Equals(Environment.GetEnvironmentVariable("ARMADA_TEST_NO_DOCKER"), "1", StringComparison.Ordinal)) return "ARMADA_TEST_NO_DOCKER=1";
            int code = DockerCommand.Run("info --format {{.ServerVersion}}", 15000, out string output);
            if (code != 0) return "Docker is not available (" + (String.IsNullOrWhiteSpace(output) ? "docker info failed" : Clip(output)) + ")";
            return null;
        }

        private static string? DockerImageProblem(string[] images, string envVar, string name)
        {
            string? docker = _DockerProblem.Value;
            if (docker != null) return docker;
            if (PickImage(images, envVar) != null) return null;
            string? overridden = Environment.GetEnvironmentVariable(envVar);
            string wanted = String.IsNullOrWhiteSpace(overridden) ? images[0] : overridden;
            return "No " + name + " image is present locally (docker pull " + wanted + ", or set " + envVar + ")";
        }

        private static readonly ConcurrentDictionary<string, string?> _ImageChoice = new ConcurrentDictionary<string, string?>();

        private static string? PickImage(string[] images, string envVar)
        {
            return _ImageChoice.GetOrAdd(envVar, _ =>
            {
                List<string> candidates = new List<string>();
                string? overridden = Environment.GetEnvironmentVariable(envVar);
                if (!String.IsNullOrWhiteSpace(overridden)) candidates.Add(overridden.Trim());
                else candidates.AddRange(images);
                foreach (string image in candidates)
                {
                    if (DockerCommand.Run("image inspect --format {{.Id}} " + image, 15000, out _) == 0) return image;
                }
                return null;
            });
        }

        private static void RunContainer(string arguments)
        {
            int code = DockerCommand.Run("run -d --rm --label " + Label + " " + arguments, 120000, out string output);
            if (code != 0) throw new InvalidOperationException("docker run failed: " + Clip(output));
            string id = output.Trim().Split('\n')[^1].Trim();
            _Containers.Add(id);
        }

        private static async Task WaitTcpAsync(int port, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
                        return;
                    }
                }
                catch (SocketException) when (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
        }

        private static async Task WaitHttpAsync(string url, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
            {
                while (true)
                {
                    try
                    {
                        HttpResponseMessage response = await http.GetAsync(url).ConfigureAwait(false);
                        if ((int)response.StatusCode < 500) return;
                    }
                    catch (Exception) when (DateTime.UtcNow < deadline)
                    {
                    }
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException(url + " did not become ready.");
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        private static async Task RetryAsync(Func<Task> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await action().ConfigureAwait(false);
                    return;
                }
                catch (Exception) when (attempt < 20)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        private static string Track(string path)
        {
            _Directories.Add(path);
            return path;
        }

        private static void HookExit()
        {
            if (Interlocked.Exchange(ref _ExitHooked, 1) == 1) return;
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Shutdown();
        }

        private static void Shutdown()
        {
            foreach (string id in _Containers) DockerCommand.Run("rm -f " + id, 30000, out _);
            foreach (IAsyncDisposable server in _Servers)
            {
                try { server.DisposeAsync().AsTask().Wait(5000); } catch (Exception) { }
            }
            foreach (string path in _Directories) TestTemp.TryDelete(path);
        }

        private static string Clip(string text)
        {
            string single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return single.Length > 200 ? single.Substring(0, 200) + "..." : single;
        }

        #endregion
    }
}
