using System;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentProxyV2Fixture : CoreTest<QBittorrentProxyV2>
    {
        [TestCase(false, "false")]
        [TestCase(true, "true")]
        public void should_explicitly_send_delete_files(bool removeData, string expected)
        {
            HttpRequest request = null;
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => request = r)
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), Array.Empty<byte>()));

            Subject.RemoveTorrent("torrent-hash", removeData, new QBittorrentSettings { Host = "localhost", Port = 8080 });

            request.Method.Should().Be(HttpMethod.Post);
            request.Url.FullUri.Should().EndWith("/api/v2/torrents/delete");
            Encoding.UTF8.GetString(request.ContentData).Should().Be("hashes=torrent-hash&deleteFiles=" + expected);
        }
    }
}
