using System.Text.RegularExpressions;

namespace AIUsageOverlay.Services.Parsing
{
    /// <summary>
    /// major.minor.patch の3成分を必須とする軽量な Semantic Version を表す。
    /// <see cref="System.Version"/> は2成分の版も受理し、SemVer の prerelease
    /// 優先順位を扱えないため、更新判定ではこの型だけを使用する。
    /// </summary>
    public sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
    {
        /// <summary>
        /// 先頭の任意の v、先頭ゼロを許さない3つの数値成分、空要素を許さない
        /// prerelease と build metadata だけを受理する。過去タグの v1.40 のような
        /// 2成分表記や 2.0.0-alpha..1、2.0.0-01 は受理しない。
        /// </summary>
        private static readonly Regex VersionPattern = new(
            @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>メジャーバージョン。</summary>
        public int Major { get; }

        /// <summary>マイナーバージョン。</summary>
        public int Minor { get; }

        /// <summary>パッチバージョン。</summary>
        public int Patch { get; }

        /// <summary>ハイフン以降の prerelease 識別子。安定版では null。</summary>
        public string? Prerelease { get; }

        /// <summary>プラス記号以降の build metadata。優先順位の比較には使用しない。</summary>
        public string? BuildMetadata { get; }

        /// <summary>
        /// 検証済みの各成分から Semantic Version を生成する。
        /// 外部入力は必ず <see cref="TryParse(string?, out SemVer?)"/> を通す。
        /// </summary>
        private SemVer(
            int major,
            int minor,
            int patch,
            string? prerelease,
            string? buildMetadata)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            BuildMetadata = buildMetadata;
        }

        /// <summary>
        /// 文字列を3成分の Semantic Version として解析する。
        /// 数値成分が不足している、不正な文字を含む、または <see cref="int"/> の
        /// 範囲を超える場合は false を返し、比較対象から除外できるようにする。
        /// </summary>
        /// <param name="value">v 接頭辞を任意で含むバージョン文字列。</param>
        /// <param name="version">成功時の解析結果。失敗時は null。</param>
        /// <returns>厳密な3成分形式として解析できた場合は true。</returns>
        public static bool TryParse(string? value, out SemVer? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var match = VersionPattern.Match(value);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out var major)
                || !int.TryParse(match.Groups[2].Value, out var minor)
                || !int.TryParse(match.Groups[3].Value, out var patch))
            {
                return false;
            }

            var normalized = value[0] is 'v' or 'V' ? value[1..] : value;
            var buildSeparator = normalized.IndexOf('+');
            var buildMetadata = buildSeparator >= 0
                ? normalized[(buildSeparator + 1)..]
                : null;
            var withoutBuild = buildSeparator >= 0
                ? normalized[..buildSeparator]
                : normalized;
            var prereleaseSeparator = withoutBuild.IndexOf('-');
            var prerelease = prereleaseSeparator >= 0
                ? withoutBuild[(prereleaseSeparator + 1)..]
                : null;

            version = new SemVer(major, minor, patch, prerelease, buildMetadata);
            return true;
        }

        /// <summary>
        /// Semantic Version の優先順位を比較する。
        /// 数値3成分の後に prerelease を比較し、同じ数値なら安定版を prerelease
        /// より新しいものとして扱う。build metadata は優先順位に影響しない。
        /// </summary>
        /// <param name="other">比較対象。</param>
        /// <returns>この版が古ければ負、同順位なら0、新しければ正。</returns>
        public int CompareTo(SemVer? other)
        {
            if (other is null)
                return 1;

            var result = Major.CompareTo(other.Major);
            if (result != 0)
                return result;

            result = Minor.CompareTo(other.Minor);
            if (result != 0)
                return result;

            result = Patch.CompareTo(other.Patch);
            if (result != 0)
                return result;

            return ComparePrerelease(Prerelease, other.Prerelease);
        }

        /// <summary>
        /// SemVer の prerelease 識別子列を比較する。
        /// 数値識別子は数値として比較し、数値識別子は非数値識別子より低い優先度とする。
        /// </summary>
        private static int ComparePrerelease(string? left, string? right)
        {
            if (left is null && right is null)
                return 0;
            if (left is null)
                return 1;
            if (right is null)
                return -1;

            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            var sharedLength = Math.Min(leftParts.Length, rightParts.Length);

            for (var index = 0; index < sharedLength; index++)
            {
                var result = ComparePrereleaseIdentifier(leftParts[index], rightParts[index]);
                if (result != 0)
                    return result;
            }

            return leftParts.Length.CompareTo(rightParts.Length);
        }

        /// <summary>
        /// prerelease の単一識別子を SemVer の規則に従って比較する。
        /// 巨大な数値識別子でもオーバーフローしないよう、先頭ゼロを除いた桁数と
        /// 文字列の順で比較する。
        /// </summary>
        private static int ComparePrereleaseIdentifier(string left, string right)
        {
            var leftIsNumeric = left.Length > 0 && left.All(char.IsDigit);
            var rightIsNumeric = right.Length > 0 && right.All(char.IsDigit);

            if (leftIsNumeric && rightIsNumeric)
            {
                var normalizedLeft = left.TrimStart('0');
                var normalizedRight = right.TrimStart('0');
                normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
                normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;

                var lengthResult = normalizedLeft.Length.CompareTo(normalizedRight.Length);
                return lengthResult != 0
                    ? lengthResult
                    : string.CompareOrdinal(normalizedLeft, normalizedRight);
            }

            if (leftIsNumeric)
                return -1;
            if (rightIsNumeric)
                return 1;

            return string.CompareOrdinal(left, right);
        }

        /// <summary>
        /// 優先順位が同じ Semantic Version かを返す。
        /// build metadata は SemVer の優先順位に影響しないため比較対象外とする。
        /// </summary>
        public bool Equals(SemVer? other) => CompareTo(other) == 0;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is SemVer other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Major);
            hash.Add(Minor);
            hash.Add(Patch);

            if (Prerelease is null)
                return hash.ToHashCode();

            foreach (var identifier in Prerelease.Split('.'))
            {
                var isNumeric = identifier.Length > 0 && identifier.All(char.IsDigit);
                hash.Add(isNumeric);
                hash.Add(
                    isNumeric ? NormalizeNumericIdentifier(identifier) : identifier,
                    StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// 数値 prerelease 識別子を比較・ハッシュ生成で共有する表現に正規化する。
        /// 先頭ゼロだけを除去し、ゼロ自体は "0" として保持する。
        /// </summary>
        private static string NormalizeNumericIdentifier(string identifier)
        {
            var normalized = identifier.TrimStart('0');
            return normalized.Length == 0 ? "0" : normalized;
        }

        /// <summary>v 接頭辞を除いた正規化済みのバージョン文字列を返す。</summary>
        public override string ToString()
        {
            var prerelease = Prerelease is null ? "" : $"-{Prerelease}";
            var buildMetadata = BuildMetadata is null ? "" : $"+{BuildMetadata}";
            return $"{Major}.{Minor}.{Patch}{prerelease}{buildMetadata}";
        }

        /// <summary>左辺が右辺より新しいかを返す。</summary>
        public static bool operator >(SemVer left, SemVer right) => left.CompareTo(right) > 0;

        /// <summary>左辺が右辺より古いかを返す。</summary>
        public static bool operator <(SemVer left, SemVer right) => left.CompareTo(right) < 0;

        /// <summary>左辺が右辺以上かを返す。</summary>
        public static bool operator >=(SemVer left, SemVer right) => left.CompareTo(right) >= 0;

        /// <summary>左辺が右辺以下かを返す。</summary>
        public static bool operator <=(SemVer left, SemVer right) => left.CompareTo(right) <= 0;
    }
}
