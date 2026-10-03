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
/// </summary>
public sealed class RequestInputRecorder
{
    private readonly ConcurrentDictionary<string, byte> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _query = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cookies = new(StringComparer.Ordinal);
    private int _requests;

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
        public ICollection<string> Keys => inner.Keys;
        public ICollection<StringValues> Values => inner.Values;
        public int Count => inner.Count;
        public bool IsReadOnly => inner.IsReadOnly;
#pragma warning disable ASP0019 // A faithful wrapper: it forwards exactly what the caller asked for.
        public void Add(string key, StringValues value) => inner.Add(key, value);
#pragma warning restore ASP0019
        public void Add(KeyValuePair<string, StringValues> item) => inner.Add(item);
        public void Clear() => inner.Clear();
        public bool Contains(KeyValuePair<string, StringValues> item) => inner.Contains(item);

        public bool ContainsKey(string key)
        {
            recorder.Header(key);
            return inner.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => inner.GetEnumerator();
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
                var current = inner.Query;
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
        public ICollection<string> Keys => inner.Keys;

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

        public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => inner.GetEnumerator();
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
        public ICollection<string> Keys => inner.Keys;

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

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
