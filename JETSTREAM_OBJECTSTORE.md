# JetStream & ObjectStore Support

## Overview

NatsWebSocket v1.2.0 adds JetStream and ObjectStore support, enabling file storage operations over NATS for .NET Framework 4.6.2+ and .NET Standard 2.0 applications.

## Features

### ObjectStore Operations
- **Put** - Upload objects with automatic chunking (128KB default)
- **Get** - Download objects with SHA-256 digest verification
- **Delete** - Soft-delete with metadata tombstone
- **List** - Enumerate all objects in a bucket
- **Exists** - Check if an object exists
- **GetInfo** - Get object metadata without downloading

### JetStream Operations
- Stream creation, deletion, and info
- Message publishing with acknowledgment
- Direct message retrieval
- Stream purging with filters

## Usage

```csharp
// Create contexts
var connection = new NatsConnection(options);
await connection.ConnectAsync();

var js = new NatsJSContext(connection);
var objContext = new NatsObjContext(js);

// Create or get a bucket
var store = await objContext.GetOrCreateObjectStoreAsync(new ObjectStoreConfig
{
    Bucket = "my-bucket",
    Description = "My object store",
    Storage = "file",
    Replicas = 1
});

// Upload an object
var info = await store.PutAsync("documents/file.pdf", fileBytes);
Console.WriteLine($"Uploaded: {info.Name}, Size: {info.Size}, Digest: {info.Digest}");

// Upload with metadata
var meta = new ObjectMeta
{
    Name = "documents/file.pdf",
    Description = "Important document",
    Metadata = new Dictionary<string, string>
    {
        { "author", "john" },
        { "version", "1.0" }
    }
};
using (var stream = File.OpenRead(filePath))
{
    info = await store.PutAsync(meta, stream);
}

// Download an object
var data = await store.GetBytesAsync("documents/file.pdf");

// Or stream to a file
using (var fs = File.Create("output.pdf"))
{
    await store.GetAsync("documents/file.pdf", fs);
}

// List all objects
var objects = await store.ListAsync();
foreach (var obj in objects)
{
    Console.WriteLine($"{obj.Name}: {obj.Size} bytes");
}

// Check existence
if (await store.ExistsAsync("documents/file.pdf"))
{
    // Object exists
}

// Delete an object
await store.DeleteAsync("documents/file.pdf");

// Delete the bucket
await objContext.DeleteObjectStoreAsync("my-bucket");
```

## Protocol Compliance

This implementation follows the NATS ObjectStore protocol (ADR-20):

- Stream naming: `OBJ_<bucket>`
- Chunk subjects: `$O.<bucket>.C.<nuid>`
- Metadata subjects: `$O.<bucket>.M.<base64url_encoded_name>`
- Metadata uses `Nats-Rollup: sub` header for latest-only semantics
- SHA-256 digest format: `SHA-256=<base64>`
- Default chunk size: 128KB

## Known Limitations

### Not Yet Implemented

1. **Watch functionality** - Cannot subscribe to object change notifications
2. **Link objects** - ObjectStore symlink-like references not supported
3. **Bucket status** - No API to get bucket statistics
4. **Seal bucket** - Cannot seal a bucket to make it read-only

### Potential Issues

1. **List pagination** - For buckets with 10,000+ objects, the list operation may return partial results. NATS paginates large subject lists, and pagination is not yet handled.

2. **No retry logic** - Network failures during operations are not automatically retried. Consider wrapping calls with Polly or similar retry policies for production use.

3. **Large files** - Files larger than available memory may cause pressure during SHA-256 computation. The implementation streams chunks but computes digest incrementally.

4. **Concurrent modifications** - The list operation uses stream subject enumeration rather than ordered consumers. Concurrent modifications during listing may cause inconsistent results.

5. **Custom JSON serializer** - Uses a lightweight custom JSON implementation rather than System.Text.Json (not available in .NET Framework). Edge cases with complex nested structures may exist.

### Verified Working

- Basic CRUD operations (Put, Get, Delete)
- Chunked uploads for files larger than 128KB
- SHA-256 digest computation and verification
- Metadata with custom headers and key-value pairs
- Object listing for reasonable bucket sizes
- Atomic uploads with rollback on failure

## Test Coverage

77 unit tests covering:
- JSON serialization/deserialization (26 tests)
- Base64 URL encoding/decoding (14 tests)
- NUID generation and uniqueness (4 tests)
- ObjectInfo serialization (13 tests)
- Special character handling
- Round-trip data integrity

## Dependencies

- BouncyCastle.Cryptography (for NKey authentication)
- No additional dependencies for ObjectStore

## Version History

### v1.2.0
- Added JetStream context with stream management
- Added ObjectStore with Put/Get/Delete/List operations
- Added 77 unit tests for new functionality
- Fixed JsonSerializer to handle Dictionary types

### v1.1.0
- Initial WebSocket NATS client

## Contributing

Integration tests against a real NATS server would be valuable. The following scenarios need verification:
- Chunk retrieval using `Nats-Sequence` header
- Large file uploads (>1GB)
- Concurrent Put/Get operations
- List pagination for large buckets
