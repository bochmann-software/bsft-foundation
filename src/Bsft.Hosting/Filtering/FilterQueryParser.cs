using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Filtering;

/// <summary>
/// Deserializes the <c>filter</c> query value into a <see cref="FilterNode"/> over the AST's own
/// <c>[JsonDerivedType]</c> vocabulary, rejecting anything that breaches the transport bounds as a
/// client-safe <see cref="ValidationException"/> rather than letting it become a crash or a 500.
/// </summary>
public sealed class FilterQueryParser : IFilterQueryParser
{
    /// <summary>The query-parameter name and the validation-error key the client reads its mistake under.</summary>
    internal const string FilterKey = "filter";

    private readonly BsftFilterTransportOptions _options;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterQueryParser"/> class.
    /// </summary>
    /// <param name="options">The transport bounds.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public FilterQueryParser(IOptions<BsftFilterTransportOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;

        // A dedicated, self-contained set of options: the filter wire format is a fixed contract that
        // must not drift with the host's JSON settings. The polymorphic discriminator and the operator
        // converter come from the AST's own attributes; all this adds is case-insensitive matching and
        // the hard recursion guard that keeps a nested payload from overflowing the reader's stack.
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            MaxDepth = _options.MaxJsonReaderDepth
        };
    }

    /// <inheritdoc />
    public FilterNode? Parse(string? rawFilter)
    {
        if (string.IsNullOrWhiteSpace(rawFilter))
            return null;

        if (rawFilter.Length > _options.MaxFilterLength)
            throw Rejected(
                $"The filter is too long: {rawFilter.Length} characters exceeds the {_options.MaxFilterLength}-character limit.");

        var node = Deserialize(rawFilter);
        if (node is null)
            throw Rejected("The filter parameter must not be JSON null.");

        EnforceShapeBounds(node);
        return node;
    }

    /// <summary>
    /// Binds the raw parameter to the node tree, or answers nothing when it does not bind.
    /// </summary>
    /// <param name="rawFilter">The filter parameter as it arrived.</param>
    /// <returns>The tree, or <see langword="null"/> when the payload was not a filter.</returns>
    private FilterNode? Deserialize(string rawFilter)
    {
        try
        {
            return JsonSerializer.Deserialize<FilterNode>(rawFilter, _serializerOptions);
        }
        catch (JsonException)
        {
            // Covers malformed JSON, an unknown discriminator, a missing required member and a payload
            // nested past the reader's MaxDepth. The reason is not echoed back verbatim: a parser error
            // can carry byte offsets and fragments of the input, which the client neither needs nor
            // should be handed.
            throw Rejected(
                "The filter parameter is not a valid filter expression: it must be well-formed JSON over the filter vocabulary and within the allowed nesting depth.");
        }
    }

    // Walks the AST iteratively — never recursively — so validating a filter can itself never overflow
    // the stack, and counts nodes and measures depth against the configured bounds.
    private void EnforceShapeBounds(FilterNode root)
    {
        var nodeCount = 0;
        var pending = new Stack<(FilterNode Node, int Depth)>();
        pending.Push((root, 1));

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();

            nodeCount++;
            if (nodeCount > _options.MaxNodeCount)
                throw Rejected(
                    $"The filter has too many conditions: it may contain at most {_options.MaxNodeCount} nodes.");

            if (depth > _options.MaxDepth)
                throw Rejected(
                    $"The filter is nested too deeply: it may nest at most {_options.MaxDepth} levels of and/or combinations.");

            PushChildren(node, depth, pending);
        }
    }

    /// <summary>
    /// Queues a node's children for the bounds walk.
    /// </summary>
    /// <param name="node">The node being descended into.</param>
    /// <param name="depth">How deep that node sits.</param>
    /// <param name="pending">The nodes still to visit.</param>
    /// <remarks>
    /// An explicit stack rather than recursion: the depth bound is checked as the walk goes, so a
    /// payload nested deeply enough to matter is rejected rather than taking the thread's stack with
    /// it on the way to finding out.
    /// </remarks>
    private static void PushChildren(FilterNode node, int depth, Stack<(FilterNode Node, int Depth)> pending)
    {
        switch (node)
        {
            case AndNode andNode:
                foreach (var child in andNode.And)
                    pending.Push((child, depth + 1));
                break;
            case OrNode orNode:
                foreach (var child in orNode.Or)
                    pending.Push((child, depth + 1));
                break;
            case ComparisonNode:
            case PresenceNode:
                // A leaf; nothing to push.
                break;
            default:
                // A bare base FilterNode (a payload with no discriminator) or any type outside the
                // closed set is not a node the compiler can turn into a predicate: reject it here as a
                // bad request rather than let it reach the compiler and become a 500.
                throw Rejected(
                    "The filter contains a node with no recognized type; every node must be a comparison, and, or or.");
        }
    }

    private static ValidationException Rejected(string message) =>
        new(new Dictionary<string, string[]> { [FilterKey] = [message] });
}
