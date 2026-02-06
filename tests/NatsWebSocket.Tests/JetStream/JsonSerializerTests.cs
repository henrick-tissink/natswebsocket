using System;
using System.Collections.Generic;
using NatsWebSocket.JetStream.Internal;
using NatsWebSocket.ObjectStore.Models;
using Xunit;

namespace NatsWebSocket.Tests.JetStream
{
    public class JsonSerializerTests
    {
        #region Serialization Tests

        [Fact]
        public void Serialize_SimpleObject_ProducesValidJson()
        {
            var obj = new TestObject { Name = "test", Count = 42 };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\"name\":\"test\"", json);
            Assert.Contains("\"count\":42", json);
        }

        [Fact]
        public void Serialize_WithNull_ReturnsNullString()
        {
            var json = JsonSerializer.Serialize(null);
            Assert.Equal("null", json);
        }

        [Fact]
        public void Serialize_StringWithSpecialChars_EscapesCorrectly()
        {
            var obj = new TestObject { Name = "line1\nline2\ttab\"quote\\backslash" };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\\n", json);
            Assert.Contains("\\t", json);
            Assert.Contains("\\\"", json);
            Assert.Contains("\\\\", json);
        }

        [Fact]
        public void Serialize_WithList_ProducesJsonArray()
        {
            var obj = new TestObjectWithList { Items = new List<string> { "a", "b", "c" } };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\"items\":[\"a\",\"b\",\"c\"]", json);
        }

        [Fact]
        public void Serialize_WithDictionary_ProducesJsonObject()
        {
            var obj = new TestObjectWithDict
            {
                Metadata = new Dictionary<string, string>
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                }
            };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\"metadata\":{", json);
            Assert.Contains("\"key1\":\"value1\"", json);
            Assert.Contains("\"key2\":\"value2\"", json);
        }

        [Fact]
        public void Serialize_WithNestedObject_ProducesNestedJson()
        {
            var obj = new TestObjectWithNested
            {
                Nested = new TestObject { Name = "inner", Count = 99 }
            };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\"nested\":{", json);
            Assert.Contains("\"name\":\"inner\"", json);
            Assert.Contains("\"count\":99", json);
        }

        [Fact]
        public void Serialize_WithBoolean_ProducesCorrectJson()
        {
            var obj = new TestObjectWithBool { IsEnabled = true, IsDisabled = false };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\"is_enabled\":true", json);
            Assert.Contains("\"is_disabled\":false", json);
        }

        [Fact]
        public void Serialize_WithDateTime_ProducesIso8601()
        {
            var dt = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
            var obj = new TestObjectWithDateTime { Created = dt };
            var json = JsonSerializer.Serialize(obj);

            Assert.Contains("\"created\":\"2025-06-15T10:30:00.000Z\"", json);
        }

        [Fact]
        public void Serialize_SkipsNullValues()
        {
            var obj = new TestObject { Name = null, Count = 5 };
            var json = JsonSerializer.Serialize(obj);

            Assert.DoesNotContain("\"name\"", json);
            Assert.Contains("\"count\":5", json);
        }

        [Fact]
        public void Serialize_SkipsEmptyLists()
        {
            var obj = new TestObjectWithList { Items = new List<string>() };
            var json = JsonSerializer.Serialize(obj);

            Assert.DoesNotContain("\"items\"", json);
        }

        #endregion

        #region Deserialization Tests

        [Fact]
        public void Deserialize_SimpleObject_ParsesCorrectly()
        {
            var json = "{\"name\":\"test\",\"count\":42}";
            var obj = JsonSerializer.Deserialize<TestObject>(json);

            Assert.Equal("test", obj.Name);
            Assert.Equal(42, obj.Count);
        }

        [Fact]
        public void Deserialize_SnakeCase_MapsToProperties()
        {
            var json = "{\"is_enabled\":true,\"is_disabled\":false}";
            var obj = JsonSerializer.Deserialize<TestObjectWithBool>(json);

            Assert.True(obj.IsEnabled);
            Assert.False(obj.IsDisabled);
        }

        [Fact]
        public void Deserialize_WithEscapedChars_UnescapesCorrectly()
        {
            var json = "{\"name\":\"line1\\nline2\\ttab\"}";
            var obj = JsonSerializer.Deserialize<TestObject>(json);

            Assert.Equal("line1\nline2\ttab", obj.Name);
        }

        [Fact]
        public void Deserialize_WithUnicodeEscape_ParsesCorrectly()
        {
            var json = "{\"name\":\"hello\\u0020world\"}";
            var obj = JsonSerializer.Deserialize<TestObject>(json);

            Assert.Equal("hello world", obj.Name);
        }

        [Fact]
        public void Deserialize_WithList_ParsesArray()
        {
            var json = "{\"items\":[\"a\",\"b\",\"c\"]}";
            var obj = JsonSerializer.Deserialize<TestObjectWithList>(json);

            Assert.Equal(3, obj.Items.Count);
            Assert.Equal("a", obj.Items[0]);
            Assert.Equal("b", obj.Items[1]);
            Assert.Equal("c", obj.Items[2]);
        }

        [Fact]
        public void Deserialize_WithDictionaryStringString_ParsesCorrectly()
        {
            var json = "{\"metadata\":{\"key1\":\"value1\",\"key2\":\"value2\"}}";
            var obj = JsonSerializer.Deserialize<TestObjectWithDict>(json);

            Assert.NotNull(obj.Metadata);
            Assert.Equal(2, obj.Metadata.Count);
            Assert.Equal("value1", obj.Metadata["key1"]);
            Assert.Equal("value2", obj.Metadata["key2"]);
        }

        [Fact]
        public void Deserialize_WithDictionaryStringListString_ParsesCorrectly()
        {
            var json = "{\"headers\":{\"Content-Type\":[\"application/json\"],\"Accept\":[\"text/html\",\"text/plain\"]}}";
            var obj = JsonSerializer.Deserialize<TestObjectWithHeaders>(json);

            Assert.NotNull(obj.Headers);
            Assert.Equal(2, obj.Headers.Count);
            Assert.Single(obj.Headers["Content-Type"]);
            Assert.Equal("application/json", obj.Headers["Content-Type"][0]);
            Assert.Equal(2, obj.Headers["Accept"].Count);
        }

        [Fact]
        public void Deserialize_WithNestedObject_ParsesCorrectly()
        {
            var json = "{\"nested\":{\"name\":\"inner\",\"count\":99}}";
            var obj = JsonSerializer.Deserialize<TestObjectWithNested>(json);

            Assert.NotNull(obj.Nested);
            Assert.Equal("inner", obj.Nested.Name);
            Assert.Equal(99, obj.Nested.Count);
        }

        [Fact]
        public void Deserialize_WithNumbers_HandlesIntAndLong()
        {
            var json = "{\"count\":42,\"big_number\":9999999999999}";
            var obj = JsonSerializer.Deserialize<TestObjectWithNumbers>(json);

            Assert.Equal(42, obj.Count);
            Assert.Equal(9999999999999L, obj.BigNumber);
        }

        [Fact]
        public void Deserialize_WithDouble_ParsesCorrectly()
        {
            var json = "{\"value\":3.14159}";
            var obj = JsonSerializer.Deserialize<TestObjectWithDouble>(json);

            Assert.Equal(3.14159, obj.Value, 5);
        }

        [Fact]
        public void Deserialize_EmptyJson_ReturnsNewInstance()
        {
            var obj = JsonSerializer.Deserialize<TestObject>("");
            Assert.NotNull(obj);
        }

        [Fact]
        public void Deserialize_NullJson_ReturnsNewInstance()
        {
            var obj = JsonSerializer.Deserialize<TestObject>(null);
            Assert.NotNull(obj);
        }

        [Fact]
        public void Deserialize_WithExtraFields_IgnoresThem()
        {
            var json = "{\"name\":\"test\",\"unknown_field\":\"value\",\"count\":5}";
            var obj = JsonSerializer.Deserialize<TestObject>(json);

            Assert.Equal("test", obj.Name);
            Assert.Equal(5, obj.Count);
        }

        #endregion

        #region ObjectInfo Serialization/Deserialization Tests

        [Fact]
        public void Deserialize_ObjectInfo_ParsesAllFields()
        {
            var json = @"{
                ""name"": ""test-file.txt"",
                ""bucket"": ""my-bucket"",
                ""nuid"": ""ABC123XYZ"",
                ""size"": 1024,
                ""chunks"": 2,
                ""digest"": ""SHA-256=abc123"",
                ""description"": ""Test file"",
                ""deleted"": false,
                ""options"": {
                    ""max_chunk_size"": 65536
                },
                ""metadata"": {
                    ""content-type"": ""text/plain""
                },
                ""headers"": {
                    ""X-Custom"": [""value1"", ""value2""]
                }
            }";

            var info = JsonSerializer.Deserialize<ObjectInfo>(json);

            Assert.Equal("test-file.txt", info.Name);
            Assert.Equal("my-bucket", info.Bucket);
            Assert.Equal("ABC123XYZ", info.Nuid);
            Assert.Equal(1024, info.Size);
            Assert.Equal(2, info.Chunks);
            Assert.Equal("SHA-256=abc123", info.Digest);
            Assert.Equal("Test file", info.Description);
            Assert.False(info.Deleted);
            Assert.NotNull(info.Options);
            Assert.Equal(65536, info.Options.MaxChunkSize);
            Assert.NotNull(info.Metadata);
            Assert.Equal("text/plain", info.Metadata["content-type"]);
            Assert.NotNull(info.Headers);
            Assert.Equal(2, info.Headers["X-Custom"].Count);
        }

        [Fact]
        public void Deserialize_ObjectInfo_MinimalFields()
        {
            var json = @"{""name"": ""file.txt"", ""bucket"": ""bucket1"", ""nuid"": ""123"", ""size"": 0, ""chunks"": 0, ""deleted"": false}";
            var info = JsonSerializer.Deserialize<ObjectInfo>(json);

            Assert.Equal("file.txt", info.Name);
            Assert.Equal("bucket1", info.Bucket);
            Assert.False(info.Deleted);
            Assert.Null(info.Options);
            Assert.Null(info.Metadata);
            Assert.Null(info.Headers);
        }

        #endregion

        #region ToSnakeCase Tests

        [Theory]
        [InlineData("Name", "name")]
        [InlineData("MaxChunkSize", "max_chunk_size")]
        [InlineData("IsEnabled", "is_enabled")]
        [InlineData("HTTPSConnection", "h_t_t_p_s_connection")]
        [InlineData("", "")]
        [InlineData(null, null)]
        public void ToSnakeCase_ConvertsCorrectly(string input, string expected)
        {
            var result = JsonSerializer.ToSnakeCase(input);
            Assert.Equal(expected, result);
        }

        #endregion

        #region Test Models

        public class TestObject
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }

        public class TestObjectWithList
        {
            public List<string> Items { get; set; }
        }

        public class TestObjectWithDict
        {
            public Dictionary<string, string> Metadata { get; set; }
        }

        public class TestObjectWithHeaders
        {
            public Dictionary<string, List<string>> Headers { get; set; }
        }

        public class TestObjectWithNested
        {
            public TestObject Nested { get; set; }
        }

        public class TestObjectWithBool
        {
            public bool IsEnabled { get; set; }
            public bool IsDisabled { get; set; }
        }

        public class TestObjectWithDateTime
        {
            public DateTime Created { get; set; }
        }

        public class TestObjectWithNumbers
        {
            public int Count { get; set; }
            public long BigNumber { get; set; }
        }

        public class TestObjectWithDouble
        {
            public double Value { get; set; }
        }

        #endregion
    }
}
