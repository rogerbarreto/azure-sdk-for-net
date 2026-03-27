// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading;

namespace System.ClientModel.Primitives;

/// <summary>
/// A <see cref="CancellationToken"/> decorated with <see cref="RequestOptions"/>.
/// Implicitly converts to <see cref="CancellationToken"/> for use with convenience methods.
/// </summary>
/// <remarks>
/// Created by <see cref="CancellationTokenExtensions.WithRequestOptions"/>.
/// Dispose the token to end the ambient <see cref="RequestOptionsScope"/>.
/// </remarks>
public readonly struct RequestOptionsToken : IDisposable
{
    private readonly CancellationToken _token;
    private readonly RequestOptionsScope _scope;

    internal RequestOptionsToken(CancellationToken token, RequestOptionsScope scope)
    {
        _token = token;
        _scope = scope;
    }

    /// <summary>The <see cref="CancellationToken"/> to pass to convenience methods.</summary>
    public CancellationToken Token => _token;

    /// <summary>
    /// Implicit conversion — use directly where <see cref="CancellationToken"/> is expected.
    /// </summary>
    public static implicit operator CancellationToken(RequestOptionsToken value) => value._token;

    /// <summary>Ends the ambient <see cref="RequestOptionsScope"/>.</summary>
    public void Dispose() => _scope.Dispose();
}
