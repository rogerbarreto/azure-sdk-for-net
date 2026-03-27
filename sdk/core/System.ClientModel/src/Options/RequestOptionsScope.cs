// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;

namespace System.ClientModel.Primitives;

/// <summary>
/// Scoped association of <see cref="RequestOptions"/> with the current async
/// execution context. Supports nesting — <see cref="Dispose"/> restores the parent scope.
/// </summary>
/// <remarks>
/// This type is used internally by
/// <see cref="CancellationTokenExtensions.WithRequestOptions"/> to flow
/// <see cref="RequestOptions"/> through convenience methods without changing their signatures.
/// </remarks>
internal sealed class RequestOptionsScope : IDisposable
{
    private static readonly AsyncLocal<RequestOptionsScope?> _current = new();

    /// <summary>Gets the current ambient scope, if any.</summary>
    internal static RequestOptionsScope? Current => _current.Value;

    private readonly RequestOptionsScope? _parent;

    /// <summary>Gets the <see cref="RequestOptions"/> associated with this scope.</summary>
    internal RequestOptions Options { get; }

    internal RequestOptionsScope(RequestOptions options)
    {
        _parent = _current.Value;
        Options = options;
        _current.Value = this;
    }

    /// <summary>Restores the parent scope.</summary>
    public void Dispose() => _current.Value = _parent;
}
