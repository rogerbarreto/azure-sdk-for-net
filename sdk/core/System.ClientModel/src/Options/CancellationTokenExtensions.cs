// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.ClientModel.Internal;
using System.Threading;

namespace System.ClientModel.Primitives;

/// <summary>
/// Extension methods for <see cref="CancellationToken"/> that enable associating
/// <see cref="RequestOptions"/> with convenience method calls.
/// </summary>
public static class CancellationTokenExtensions
{
    /// <summary>
    /// Associates <see cref="RequestOptions"/> with the current async execution context.
    /// All convenience method calls within this scope will use the provided options
    /// when converting <see cref="CancellationToken"/> to <see cref="RequestOptions"/>
    /// via <see cref="ToRequestOptions"/>.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to associate with.</param>
    /// <param name="options">The request options to inject into convenience method calls.</param>
    /// <returns>
    /// A <see cref="RequestOptionsToken"/> that implicitly converts to
    /// <see cref="CancellationToken"/>. Dispose it to end the scope.
    /// </returns>
    public static RequestOptionsToken WithRequestOptions(
        this CancellationToken cancellationToken,
        RequestOptions options)
    {
        Argument.AssertNotNull(options, nameof(options));

        if (cancellationToken.CanBeCanceled)
        {
            options.CancellationToken = cancellationToken;
        }

        var scope = new RequestOptionsScope(options);
        return new RequestOptionsToken(cancellationToken, scope);
    }

    /// <summary>
    /// Converts a <see cref="CancellationToken"/> to <see cref="RequestOptions"/>.
    /// If an ambient <see cref="RequestOptionsScope"/> exists (set by
    /// <see cref="WithRequestOptions"/>), returns its options instead of creating a default.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The <see cref="RequestOptions"/> from the ambient scope, or a new instance
    /// with the cancellation token set, or <c>null</c> if the token cannot be canceled.
    /// </returns>
    public static RequestOptions? ToRequestOptions(this CancellationToken cancellationToken)
    {
        if (RequestOptionsScope.Current is { } scope)
        {
            return scope.Options;
        }

        return cancellationToken.CanBeCanceled
            ? new RequestOptions { CancellationToken = cancellationToken }
            : null;
    }
}
