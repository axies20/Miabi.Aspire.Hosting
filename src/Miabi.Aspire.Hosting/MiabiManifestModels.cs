using YamlDotNet.Serialization;

namespace Miabi.Aspire.Hosting;

internal sealed class MiabiManifest
{
    [YamlMember(Alias = "apiVersion", Order = 0)]
    public string ApiVersion { get; init; } = "miabi.io/v1";

    [YamlMember(Alias = "kind", Order = 1)]
    public string Kind { get; init; } = "";

    [YamlMember(Alias = "metadata", Order = 2)]
    public MiabiMetadata Metadata { get; init; } = new();

    [YamlMember(Alias = "spec", Order = 3)]
    public object Spec { get; init; } = new();
}

internal sealed class MiabiMetadata
{
    [YamlMember(Alias = "name")]
    public string Name { get; init; } = "";

    [YamlMember(Alias = "labels", DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
    public Dictionary<string, string> Labels { get; init; } = [];
}

internal sealed class MiabiApplicationSpec
{
    [YamlMember(Alias = "image", Order = 0)]
    public string Image { get; init; } = "";

    [YamlMember(Alias = "tag", Order = 1, DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Tag { get; init; }

    [YamlMember(Alias = "ports", Order = 2, DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
    public List<MiabiPortSpec> Ports { get; init; } = [];

    [YamlMember(Alias = "env", Order = 3, DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
    public SortedDictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);

    [YamlMember(Alias = "secretEnv", Order = 4, DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
    public List<string> SecretEnvironment { get; init; } = [];

    [YamlMember(Alias = "mounts", Order = 5, DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
    public List<MiabiMountSpec> Mounts { get; init; } = [];
}

internal sealed class MiabiPortSpec
{
    [YamlMember(Alias = "container")]
    public int Container { get; init; }

    [YamlMember(Alias = "protocol")]
    public string Protocol { get; init; } = "tcp";

    [YamlMember(Alias = "scheme")]
    public string Scheme { get; init; } = "http";

    [YamlMember(Alias = "publish", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Publish { get; init; }

    [YamlMember(Alias = "hostPort", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int HostPort { get; init; }
}

internal sealed class MiabiMountSpec
{
    [YamlMember(Alias = "volume")]
    public string Volume { get; init; } = "";

    [YamlMember(Alias = "path")]
    public string Path { get; init; } = "";

    [YamlMember(Alias = "readOnly", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool ReadOnly { get; init; }
}

internal sealed class MiabiVolumeSpec
{
    [YamlMember(Alias = "size", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Size { get; init; }
}

internal sealed class MiabiDomainSpec
{
    [YamlMember(Alias = "tls")]
    public string Tls { get; init; } = "acme";
}

internal sealed class MiabiRouteSpec
{
    [YamlMember(Alias = "hosts")]
    public string[] Hosts { get; init; } = [];

    [YamlMember(Alias = "app")]
    public string Application { get; init; } = "";

    [YamlMember(Alias = "port")]
    public int Port { get; init; }

    [YamlMember(Alias = "tls")]
    public string Tls { get; init; } = "acme";
}
