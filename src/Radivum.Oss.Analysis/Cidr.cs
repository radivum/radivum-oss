namespace Radivum.Oss.Analysis;

/// <summary>
/// Minimum IPv4 prefix handling the open-source deterministic checks need: parse, prefix
/// normalization, and textual prefix overlap. Routing-grade arithmetic - range identity, address
/// containment, and the reachability helpers built on them - is premium analysis capability and
/// deliberately does not live here, so each edition's arithmetic stays independently correct and
/// independently tested.
/// </summary>
public static class Cidr
{
    /// <summary>
    /// Parses a dotted-quad IPv4 CIDR block such as <c>10.0.0.0/16</c> into its masked network
    /// address and network mask.
    /// </summary>
    /// <param name="cidr">Candidate CIDR block.</param>
    /// <param name="network">Masked network address when parsing succeeds; otherwise zero.</param>
    /// <param name="mask">Network mask when parsing succeeds; otherwise zero.</param>
    /// <returns><see langword="true"/> when the input is a well-formed IPv4 CIDR block.</returns>
    public static bool TryParse(string? cidr, out uint network, out uint mask)
    {
        network = 0;
        mask = 0;

        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        var octets = parts[0].Split('.');
        if (octets.Length != 4)
        {
            return false;
        }

        uint ip = 0;
        foreach (var octet in octets)
        {
            if (!byte.TryParse(octet, out var parsed))
            {
                return false;
            }

            ip = (ip << 8) | parsed;
        }

        if (!int.TryParse(parts[1], out var bits) || bits is < 0 or > 32)
        {
            return false;
        }

        mask = bits == 0 ? 0U : uint.MaxValue << (32 - bits);
        network = ip & mask;
        return true;
    }

    /// <summary>
    /// Determines whether two textual CIDR blocks share any address. Unparseable input never
    /// overlaps.
    /// </summary>
    /// <param name="left">First CIDR block.</param>
    /// <param name="right">Second CIDR block.</param>
    /// <returns><see langword="true"/> when both parse and the prefixes overlap.</returns>
    public static bool Overlaps(string? left, string? right)
    {
        if (!TryParse(left, out var leftNetwork, out var leftMask) ||
            !TryParse(right, out var rightNetwork, out var rightMask))
        {
            return false;
        }

        return Overlaps(leftNetwork, leftMask, rightNetwork, rightMask);
    }

    private static bool Overlaps(uint leftNetwork, uint leftMask, uint rightNetwork, uint rightMask)
    {
        var leftEnd = leftNetwork | ~leftMask;
        var rightEnd = rightNetwork | ~rightMask;

        return leftNetwork <= rightEnd && rightNetwork <= leftEnd;
    }
}
