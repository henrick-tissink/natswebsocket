using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NatsWebSocket.JetStream.Internal;
using NatsWebSocket.ObjectStore;
using NatsWebSocket.ObjectStore.Models;
using Xunit;

namespace NatsWebSocket.Tests.ObjectStore
{
    public class ObjectStoreSerializationTests
    {
        #region SerializeObjectInfo Tests

        [Fact]
        public void SerializeObjectInfo_MinimalFields_ProducesValidJson()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 100,
                Chunks = 1,
                Deleted = false
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"name\":\"test.txt\"", json);
            Assert.Contains("\"bucket\":\"mybucket\"", json);
            Assert.Contains("\"nuid\":\"ABC123\"", json);
            Assert.Contains("\"size\":100", json);
            Assert.Contains("\"chunks\":1", json);
            Assert.Contains("\"deleted\":false", json);
        }

        [Fact]
        public void SerializeObjectInfo_WithDigest_IncludesDigest()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 100,
                Chunks = 1,
                Digest = "SHA-256=dGVzdA==",
                Deleted = false
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"digest\":\"SHA-256=dGVzdA==\"", json);
        }

        [Fact]
        public void SerializeObjectInfo_WithDescription_IncludesDescription()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 0,
                Chunks = 0,
                Description = "This is a test file",
                Deleted = false
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"description\":\"This is a test file\"", json);
        }

        [Fact]
        public void SerializeObjectInfo_WithOptions_IncludesOptions()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 0,
                Chunks = 0,
                Deleted = false,
                Options = new ObjectOptions { MaxChunkSize = 65536 }
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"options\":{\"max_chunk_size\":65536}", json);
        }

        [Fact]
        public void SerializeObjectInfo_WithMetadata_IncludesMetadata()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 0,
                Chunks = 0,
                Deleted = false,
                Metadata = new Dictionary<string, string>
                {
                    { "content-type", "text/plain" },
                    { "author", "test" }
                }
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"metadata\":{", json);
            Assert.Contains("\"content-type\":\"text/plain\"", json);
            Assert.Contains("\"author\":\"test\"", json);
        }

        [Fact]
        public void SerializeObjectInfo_WithHeaders_IncludesHeaders()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 0,
                Chunks = 0,
                Deleted = false,
                Headers = new Dictionary<string, List<string>>
                {
                    { "X-Custom-Header", new List<string> { "value1", "value2" } }
                }
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"headers\":{", json);
            Assert.Contains("\"X-Custom-Header\":[\"value1\",\"value2\"]", json);
        }

        [Fact]
        public void SerializeObjectInfo_WithSpecialCharsInName_EscapesCorrectly()
        {
            var info = new ObjectInfo
            {
                Name = "file with \"quotes\" and\nnewline",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 0,
                Chunks = 0,
                Deleted = false
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\\\"quotes\\\"", json);
            Assert.Contains("\\n", json);
        }

        [Fact]
        public void SerializeObjectInfo_Deleted_SetsDeletedTrue()
        {
            var info = new ObjectInfo
            {
                Name = "test.txt",
                Bucket = "mybucket",
                Nuid = "ABC123",
                Size = 0,
                Chunks = 0,
                Deleted = true
            };

            var json = InvokeSerializeObjectInfo(info);

            Assert.Contains("\"deleted\":true", json);
        }

        [Fact]
        public void SerializeObjectInfo_RoundTrip_PreservesData()
        {
            var original = new ObjectInfo
            {
                Name = "test-file.txt",
                Bucket = "my-bucket",
                Nuid = "XYZ789",
                Size = 12345,
                Chunks = 5,
                Digest = "SHA-256=abc123def456",
                Description = "A test file",
                Deleted = false,
                Options = new ObjectOptions { MaxChunkSize = 32768 },
                Metadata = new Dictionary<string, string>
                {
                    { "author", "test-user" },
                    { "version", "1.0" }
                },
                Headers = new Dictionary<string, List<string>>
                {
                    { "Content-Type", new List<string> { "application/octet-stream" } }
                }
            };

            var json = InvokeSerializeObjectInfo(original);
            var deserialized = JsonSerializer.Deserialize<ObjectInfo>(json);

            Assert.Equal(original.Name, deserialized.Name);
            Assert.Equal(original.Bucket, deserialized.Bucket);
            Assert.Equal(original.Nuid, deserialized.Nuid);
            Assert.Equal(original.Size, deserialized.Size);
            Assert.Equal(original.Chunks, deserialized.Chunks);
            Assert.Equal(original.Digest, deserialized.Digest);
            Assert.Equal(original.Description, deserialized.Description);
            Assert.Equal(original.Deleted, deserialized.Deleted);
            Assert.Equal(original.Options.MaxChunkSize, deserialized.Options.MaxChunkSize);
            Assert.Equal(original.Metadata["author"], deserialized.Metadata["author"]);
            Assert.Equal(original.Headers["Content-Type"][0], deserialized.Headers["Content-Type"][0]);
        }

        #endregion

        #region EscapeJson Tests

        [Theory]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("hello", "hello")]
        [InlineData("hello world", "hello world")]
        [InlineData("line1\nline2", "line1\\nline2")]
        [InlineData("tab\there", "tab\\there")]
        [InlineData("quote\"here", "quote\\\"here")]
        [InlineData("back\\slash", "back\\\\slash")]
        [InlineData("carriage\rreturn", "carriage\\rreturn")]
        [InlineData("all\n\r\t\"\\together", "all\\n\\r\\t\\\"\\\\together")]
        public void EscapeJson_HandlesSpecialChars(string input, string expected)
        {
            var result = InvokeEscapeJson(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region SHA256 Digest Tests

        [Fact]
        public void DigestFormat_MatchesNatsSpec()
        {
            var data = Encoding.UTF8.GetBytes("hello world");
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                var digest = "SHA-256=" + Convert.ToBase64String(hash);

                Assert.StartsWith("SHA-256=", digest);
                // The base64 should be valid
                var base64Part = digest.Substring(8);
                var decoded = Convert.FromBase64String(base64Part);
                Assert.Equal(32, decoded.Length); // SHA-256 is 32 bytes
            }
        }

        [Fact]
        public void DigestFormat_EmptyData_ProducesValidDigest()
        {
            var data = Array.Empty<byte>();
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                var digest = "SHA-256=" + Convert.ToBase64String(hash);

                // Empty data SHA-256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
                Assert.Equal("SHA-256=47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=", digest);
            }
        }

        #endregion

        #region Helper Methods

        private static string InvokeSerializeObjectInfo(ObjectInfo info)
        {
            // Use reflection to call the private SerializeObjectInfo method
            var storeType = typeof(NatsObjStore);
            var method = storeType.GetMethod("SerializeObjectInfo",
                BindingFlags.NonPublic | BindingFlags.Static);

            return (string)method.Invoke(null, new object[] { info });
        }

        private static string InvokeEscapeJson(string s)
        {
            // Use reflection to call the private EscapeJson method
            var storeType = typeof(NatsObjStore);
            var method = storeType.GetMethod("EscapeJson",
                BindingFlags.NonPublic | BindingFlags.Static);

            return (string)method.Invoke(null, new object[] { s });
        }

        #endregion
    }
}
