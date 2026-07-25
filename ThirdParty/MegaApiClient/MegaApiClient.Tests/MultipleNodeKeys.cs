using System.Collections.Generic;
using CG.Web.MegaApiClient.Cryptography;
using CG.Web.MegaApiClient.Serialization;
using Newtonsoft.Json;
using Xunit;

namespace CG.Web.MegaApiClient.Tests
{
  public class MultipleNodeKeys
  {
    [Fact]
    public void Deserialize_UsesLaterKeyWhenFirstKeyCannotDecryptAttributes()
    {
      var masterKey = new byte[]
      {
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
      };
      var obsoleteNodeKey = new byte[]
      {
        0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
        0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F,
      };
      var validNodeKey = new byte[]
      {
        0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37,
        0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F,
      };
      var serializedAttributes = Crypto.EncryptAttributes(
        new Attributes("client.exe"),
        validNodeKey).ToBase64();
      var obsoleteSerializedKey = Crypto.EncryptKey(
        obsoleteNodeKey,
        masterKey).ToBase64();
      var validSerializedKey = Crypto.EncryptKey(
        validNodeKey,
        masterKey).ToBase64();
      var json = JsonConvert.SerializeObject(new
      {
        h = "file-node",
        p = "folder-node",
        t = NodeType.Directory,
        a = serializedAttributes,
        k = $"old-share:{obsoleteSerializedKey}/public-share:{validSerializedKey}",
      });
      var sharedKeys = new List<SharedKey>();

      var node = JsonConvert.DeserializeObject<Node>(
        json,
        new NodeConverter(masterKey, ref sharedKeys));

      Assert.NotNull(node);
      Assert.Equal("client.exe", node.Name);
      Assert.Equal(validNodeKey, node.FullKey);
    }
  }
}
