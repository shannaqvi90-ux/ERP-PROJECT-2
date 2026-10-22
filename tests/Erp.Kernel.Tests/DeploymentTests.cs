using System.Globalization;
using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Kernel.Tests;

/// <summary>The one-command deployment (compose.yaml) as shipped.</summary>
public sealed class DeploymentTests
{
    /// <summary>
    /// The database service has room in /dev/shm for parallel queries (critic p00 round 13: with
    /// Docker's default 64 MB, three people printing the 100,000-user report at once got an HTTP
    /// 500, "could not resize shared memory segment ... No space left on device"; twelve got nine).
    /// </summary>
    [Fact]
    public void The_database_service_gives_parallel_queries_shared_memory()
    {
        var compose = File.ReadAllText(Repo.PathOf("compose.yaml"));
        var db = Regex.Match(compose, @"^  db:\r?\n(?<body>(?:^(?:    .*|\s*)\r?\n)+)", RegexOptions.Multiline);
        Assert.True(db.Success, "compose.yaml has no db service");
        var shm = Regex.Match(db.Groups["body"].Value, @"^    shm_size:\s*""?(?<n>\d+)\s*(?<unit>[kmg])b?""?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        Assert.True(shm.Success, "the db service sets no shm_size: Docker's 64 MB default is too small for parallel hash joins over 100,000 rows");
        var megabytes = long.Parse(shm.Groups["n"].Value, CultureInfo.InvariantCulture) * char.ToLowerInvariant(shm.Groups["unit"].Value[0]) switch
        {
            'g' => 1024,
            'm' => 1,
            _ => 0,
        };
        Assert.True(megabytes >= 512, $"the db service's shm_size is {megabytes} MB; at least 512 MB");
    }
}
