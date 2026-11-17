using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;
using NzbDrone.Common.Exceptions;
using NzbDrone.Host;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class SslCertificateFixture
    {
        [TestCase("fixture-password", true)]
        [TestCase("incorrect-password", false)]
        [TestCase(null, false)]
        public void should_validate_password_protected_pfx(string password, bool valid)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfx");
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, "fixture-password"));

            try
            {
                var method = typeof(Bootstrap).GetMethod("ValidateSslCertificate", BindingFlags.NonPublic | BindingFlags.Static);
                if (valid)
                {
                    using var loaded = (X509Certificate2)method.Invoke(null, new object[] { path, password });
                    Assert.That(loaded.HasPrivateKey, Is.True);
                    Assert.That(loaded.Thumbprint, Is.EqualTo(certificate.Thumbprint));
                }
                else
                {
                    var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { path, password }));
                    Assert.That(error.InnerException, Is.TypeOf<RadarrStartupException>());
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
