using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace Erp.Testing;

/// <summary>
/// Records the name of every request header, query parameter and cookie the running application
/// reads, by anyone (middleware, model binding, handlers), for the tenant-isolation gate: an
/// attacker then sends the other tenant's identifiers in exactly the inputs the code looks at,
/// whatever they are called, instead of guessing names. Installed in every test host; it only
/// watches and changes nothing.
/// <para>
/// Code that enumerates the headers, query parameters or cookies (or reads the raw query string)
/// and picks one by a name it compares itself reads an input whose name the recorder never learns,
/// so the attack could never send tenant B's id in it (critic p00 round 4, plant T1d: a header
/// found by looping over the headers rebound the tenant). Every such enumeration by product code
/// (the innermost caller outside the base class library and ASP.NET Core) is recorded with that
/// code's type, and the isolation gate fails on it unless reviewed.
/// </para>
/// </summary>
public sealed class RequestInputRecorder
{
    private readonly ConcurrentDictionary<string, byte> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _query = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cookies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _enumerations = new(StringComparer.Ordinal);

    /// <summary>True on this thread while the query feature parses the raw query string.</summary>
    [ThreadStatic]
    private static bool _parsingQuery;
    private int _requests;

    /// <summary>Reviewed product types that may enumerate request inputs (tests/Gates/request-input-enumeration.txt).</summary>
    public const string ReviewedFile = "tests/Gates/request-input-enumeration.txt";

    /// <summary>Request inputs product code enumerated (or read raw), with the product type that did,
    /// other than the reviewed ones.</summary>
    public IReadOnlyList<string> Enumerations
    {
        get
        {
            var reviewed = Repo.ReadReviewedList(ReviewedFile).Select(e => e.Entry).ToHashSet(StringComparer.Ordinal);
            return _enumerations.Keys.Where(k => !reviewed.Contains(k.Split(' ')[^1])).Order(StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Every enumeration recorded, reviewed or not (for self-tests).</summary>
    public IReadOnlyList<string> AllEnumerations => _enumerations.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Record an enumeration of <paramref name="input"/> when product code asked for it.</summary>
    internal void Enumerated(string input)
    {
        if (ProductCaller() is { } caller)
        {
            _enumerations.TryAdd($"{input} by {caller}", 0);
        }
    }

    /// <summary>The product type that asked, or null when the base class library or ASP.NET Core
    /// did (model binding, the query and cookie parsers, routing).</summary>
    internal static string? ProductCaller()
    {
        foreach (var frame in new System.Diagnostics.StackTrace(2, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            while (type?.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }
            var ns = type?.Namespace ?? "";
            if (type is null || type == typeof(RequestInputRecorder) || ns.StartsWith("System", StringComparison.Ordinal) ||
                type.FullName is "Microsoft.AspNetCore.Http.DefaultHttpRequest" or "Microsoft.AspNetCore.Http.DefaultHttpContext" or "Microsoft.AspNetCore.Http.HttpRequest")
            {
                continue;
            }
            return ns.StartsWith("Erp.", StringComparison.Ordinal) && !ns.StartsWith("Erp.Testing", StringComparison.Ordinal) ? type.FullName : null;
        }
        return null;
    }

    /// <summary>Header names read so far.</summary>
    public IReadOnlyList<string> Headers => _headers.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Query parameter names read so far.</summary>
    public IReadOnlyList<string> QueryNames => _query.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Cookie names read so far.</summary>
    public IReadOnlyList<string> Cookies => _cookies.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Requests watched.</summary>
    public int Requests => _requests;

    internal void Header(string name) => _headers.TryAdd(name, 0);
    internal void Query(string name) => _query.TryAdd(name, 0);
    internal void Cookie(string name) => _cookies.TryAdd(name, 0);

    internal void Watch(HttpContext context)
    {
        Interlocked.Increment(ref _requests);
        var request = context.Features.Get<IHttpRequestFeature>();
        if (request is not null)
        {
            request.Headers = new RecordingHeaders(request.Headers, this);
            context.Features.Set<IHttpRequestFeature>(new RecordingRequestFeature(request, this));
        }
        var query = context.Features.Get<IQueryFeature>() ?? new QueryFeature(context.Features);
        context.Features.Set<IQueryFeature>(new RecordingQueryFeature(query, this));
        var cookies = context.Features.Get<IRequestCookiesFeature>() ?? new RequestCookiesFeature(context.Features);
        context.Features.Set<IRequestCookiesFeature>(new RecordingCookiesFeature(cookies, this));
    }

    /// <summary>Runs the recorder first in the pipeline.</summary>
    internal sealed class StartupFilter(RequestInputRecorder recorder) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, following) =>
            {
                recorder.Watch(context);
                return following(context);
            });
            next(app);
        };
    }

    private sealed class RecordingHeaders(IHeaderDictionary inner, RequestInputRecorder recorder) : IHeaderDictionary
    {
        public StringValues this[string key]
        {
            get
            {
                recorder.Header(key);
                return inner[key];
            }
            set => inner[key] = value;
        }

        StringValues IDictionary<string, StringValues>.this[string key]
        {
            get
            {
                recorder.Header(key);
                return ((IDictionary<string, StringValues>)inner)[key];
            }
            set => ((IDictionary<string, StringValues>)inner)[key] = value;
        }

        public long? ContentLength { get => inner.ContentLength; set => inner.ContentLength = value; }

        public ICollection<string> Keys
        {
            get
            {
                recorder.Enumerated("headers");
                return inner.Keys;
            }
        }

        public ICollection<StringValues> Values
        {
            get
            {
                recorder.Enumerated("headers");
                return inner.Values;
            }
        }

        public int Count => inner.Count;
        public bool IsReadOnly => inner.IsReadOnly;
#pragma warning disable ASP0019 // A faithful wrapper: it forwards exactly what the caller asked for.
        public void Add(string key, StringValues value) => inner.Add(key, value);
#pragma warning restore ASP0019
        public void Add(KeyValuePair<string, StringValues> item) => inner.Add(item);
        public void Clear() => inner.Clear();
        public bool Contains(KeyValuePair<string, StringValues> item)
        {
            recorder.Header(item.Key);
            return inner.Contains(item);
        }

        public bool ContainsKey(string key)
        {
            recorder.Header(key);
            return inner.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            recorder.Enumerated("headers");
            inner.CopyTo(array, arrayIndex);
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            recorder.Enumerated("headers");
            return inner.GetEnumerator();
        }
        public bool Remove(string key) => inner.Remove(key);
        public bool Remove(KeyValuePair<string, StringValues> item) => inner.Remove(item);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out StringValues value)
        {
            recorder.Header(key);
            return inner.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingQueryFeature(IQueryFeature inner, RequestInputRecorder recorder) : IQueryFeature
    {
        private IQueryCollection? _wrapped;
        private IQueryCollection? _source;

        public IQueryCollection Query
        {
            get
            {
                // The framework's query feature parses the raw query string into the collection the
                // first time it is asked for: that read is the parse, not code choosing an input by
                // a name it compares itself (every name read from the collection is recorded, and
                // enumerating the collection is recorded as an enumeration).
                IQueryCollection current;
                _parsingQuery = true;
                try
                {
                    current = inner.Query;
                }
                finally
                {
                    _parsingQuery = false;
                }
                if (!ReferenceEquals(current, _source))
                {
                    _source = current;
                    _wrapped = new RecordingQuery(current, recorder);
                }
                return _wrapped!;
            }
            set => inner.Query = value;
        }
    }

    private sealed class RecordingQuery(IQueryCollection inner, RequestInputRecorder recorder) : IQueryCollection
    {
        public StringValues this[string key]
        {
            get
            {
                recorder.Query(key);
                return inner[key];
            }
        }

        public int Count => inner.Count;

        public ICollection<string> Keys
        {
            get
            {
                recorder.Enumerated("query");
                return inner.Keys;
            }
        }

        public bool ContainsKey(string key)
        {
            recorder.Query(key);
            return inner.ContainsKey(key);
        }

        public bool TryGetValue(string key, out StringValues value)
        {
            recorder.Query(key);
            return inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator()
        {
            recorder.Enumerated("query");
            return inner.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingCookiesFeature(IRequestCookiesFeature inner, RequestInputRecorder recorder) : IRequestCookiesFeature
    {
        private IRequestCookieCollection? _wrapped;
        private IRequestCookieCollection? _source;

        public IRequestCookieCollection Cookies
        {
            get
            {
                var current = inner.Cookies;
                if (!ReferenceEquals(current, _source))
                {
                    _source = current;
                    _wrapped = new RecordingCookies(current, recorder);
                }
                return _wrapped!;
            }
            set => inner.Cookies = value;
        }
    }

    private sealed class RecordingCookies(IRequestCookieCollection inner, RequestInputRecorder recorder) : IRequestCookieCollection
    {
        public string? this[string key]
        {
            get
            {
                recorder.Cookie(key);
                return inner[key];
            }
        }

        public int Count => inner.Count;

        public ICollection<string> Keys
        {
            get
            {
                recorder.Enumerated("cookies");
                return inner.Keys;
            }
        }

        public bool ContainsKey(string key)
        {
            recorder.Cookie(key);
            return inner.ContainsKey(key);
        }

        public bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            recorder.Cookie(key);
            return inner.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            recorder.Enumerated("cookies");
            return inner.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The request feature with the raw query string and the raw target recorded when
    /// product code reads them (the parsed query is read through <see cref="RecordingQuery"/>).</summary>
    private sealed class RecordingRequestFeature(IHttpRequestFeature inner, RequestInputRecorder recorder) : IHttpRequestFeature
    {
        public string Protocol { get => inner.Protocol; set => inner.Protocol = value; }
        public string Scheme { get => inner.Scheme; set => inner.Scheme = value; }
        public string Method { get => inner.Method; set => inner.Method = value; }
        public string PathBase { get => inner.PathBase; set => inner.PathBase = value; }
        public string Path { get => inner.Path; set => inner.Path = value; }

        public string QueryString
        {
            get
            {
                if (!_parsingQuery)
                {
                    recorder.Enumerated("raw query string");
                }
                return inner.QueryString;
            }
            set => inner.QueryString = value;
        }

        public string RawTarget
        {
            get
            {
                recorder.Enumerated("raw request target");
                return inner.RawTarget;
            }
            set => inner.RawTarget = value;
        }

        public IHeaderDictionary Headers { get => inner.Headers; set => inner.Headers = value; }
        public Stream Body { get => inner.Body; set => inner.Body = value; }
    }
}
