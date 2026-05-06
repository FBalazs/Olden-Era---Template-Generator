using OldenEraTemplateEditor.Models;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Olden_Era___Template_Editor.Services
{
    public static class TemplatePreviewPngWriter
    {
        // Canvas size matches game's required preview resolution
        private const int Width  = 700;
        private const int Height = 700;

        // Neutral zone layout names → tier
        private const string SideLayoutName     = "zone_layout_sides";         // Bronze
        private const string TreasureLayoutName = "zone_layout_treasure_zone"; // Silver
        private const string CenterLayoutName   = "zone_layout_center";        // Gold

        private static readonly Color BackgroundColor = Color.FromRgb(28, 22, 16);

        // ── Neutral tier colours ─────────────────────────────────────────────────
        // Bronze
        private static readonly Color BronzeFill    = Color.FromRgb(101,  67,  33);
        private static readonly Color BronzeBorder  = Color.FromRgb(205, 127,  50);
        // Silver
        private static readonly Color SilverFill    = Color.FromRgb( 72,  76,  80);
        private static readonly Color SilverBorder  = Color.FromRgb(192, 192, 192);
        // Gold
        private static readonly Color GoldFill      = Color.FromRgb(120,  90,  20);
        private static readonly Color GoldBorder    = Color.FromRgb(255, 210,  50);

        // ── Spawn / player zone colours ──────────────────────────────────────────
        private static readonly Color SpawnFill    = Color.FromRgb( 42,  90,  50);
        private static readonly Color SpawnBorder  = Color.FromRgb(100, 200, 120);

        // ── Hub colour ───────────────────────────────────────────────────────────
        private static readonly Color HubFill   = Color.FromRgb(55, 80, 95);
        private static readonly Color HubBorder = Color.FromRgb(130, 180, 200);

        // ── Connection colours ───────────────────────────────────────────────────
        // Direct / Default / GladiatorArena → thick gold line
        private static readonly Color DirectLineColor = Color.FromRgb(180, 145, 60);
        // Portal → semi-transparent blue
        private static readonly Color PortalLineColor = Color.FromArgb(180, 90, 170, 210);

        // ── Radius ───────────────────────────────────────────────────────────────
        // Cap; actual radius is computed per-layout by LayoutZones().
        private const double ZoneRadiusMax = 38;
        private const double BinaryTreeInnerRadius = 36;

        // ── Human icon (person silhouette drawn with geometry) ───────────────────
        // Drawn relative to the zone centre; scaled to fit the circle.

        public static string GetSidecarPath(string rmgJsonPath) =>
            rmgJsonPath.EndsWith(".rmg.json", StringComparison.OrdinalIgnoreCase)
                ? rmgJsonPath[..^".rmg.json".Length] + ".png"
                : Path.ChangeExtension(rmgJsonPath, ".png");

        public static void Save(RmgTemplate template, string previewPath)
        {
            string? directory = Path.GetDirectoryName(previewPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var bitmap = Render(template);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string tempPath = $"{previewPath}.{Guid.NewGuid():N}.tmp";
            using (var stream = File.Create(tempPath))
                encoder.Save(stream);

            File.Move(tempPath, previewPath, overwrite: true);
        }

        /// <summary>Renders the preview to a <see cref="BitmapSource"/> without writing any files.</summary>
        public static BitmapSource Render(RmgTemplate template)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
                DrawPreview(dc, template);

            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ── Main draw ────────────────────────────────────────────────────────────

        private static void DrawPreview(DrawingContext dc, RmgTemplate template)
        {
            dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(0, 0, Width, Height));
            dc.DrawRoundedRectangle(null,
                new Pen(new SolidColorBrush(Color.FromRgb(143, 115, 63)), 3),
                new Rect(8, 8, Width - 16, Height - 16), 8, 8);

            Variant? variant = template.Variants?.FirstOrDefault();
            List<Zone> zones = variant?.Zones ?? [];
            if (zones.Count == 0)
            {
                DrawText(dc, template.Name, new Point(Width / 2.0, Height / 2.0), 24, Brushes.White, centered: true);
                return;
            }

            var orderedZones = OrderZones(zones, variant?.Orientation?.ZeroAngleZone);
            var positions    = LayoutZones(orderedZones, variant?.Connections ?? [], variant?.Orientation?.ZeroAngleZone);

            // Draw connections first (below zones)
            DrawConnections(dc, variant?.Connections ?? [], positions);

            // Draw zone circles — non-player zones first, then spawn zones on top
            // so the castle-count badge is never obscured by an adjacent circle.
            foreach (Zone zone in orderedZones.Where(z => !z.Name.StartsWith("Spawn-", StringComparison.Ordinal)))
                DrawZone(dc, zone, positions[zone.Name]);
            foreach (Zone zone in orderedZones.Where(z => z.Name.StartsWith("Spawn-", StringComparison.Ordinal)))
                DrawZone(dc, zone, positions[zone.Name]);
        }

        // ── Zone ordering / layout ───────────────────────────────────────────────

        private static List<Zone> OrderZones(List<Zone> zones, string? zeroAngleZone)
        {
            var ordered = zones.ToList();
            int zeroIndex = !string.IsNullOrWhiteSpace(zeroAngleZone)
                ? ordered.FindIndex(z => string.Equals(z.Name, zeroAngleZone, StringComparison.Ordinal))
                : -1;
            if (zeroIndex <= 0) return ordered;
            return ordered.Skip(zeroIndex).Concat(ordered.Take(zeroIndex)).ToList();
        }

        private static Dictionary<string, Point> LayoutZones(List<Zone> zones, List<Connection> connections, string? zeroAngleZone)
        {
            if (TryLayoutBinaryTree(zones, connections, zeroAngleZone, out Dictionary<string, Point>? binaryTreePositions))
                return binaryTreePositions!;

            return LayoutRingZones(zones);
        }

        internal static Dictionary<string, Point> LayoutZonesForTesting(List<Zone> zones, List<Connection> connections, string? zeroAngleZone) =>
            LayoutZones(zones, connections, zeroAngleZone);

        private static Dictionary<string, Point> LayoutRingZones(List<Zone> zones)
        {
            var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
            Zone? hub = zones.FirstOrDefault(z => string.Equals(z.Name, "Hub", StringComparison.Ordinal));
            var outer = hub is null ? zones : zones.Where(z => z != hub).ToList();

            if (hub is not null)
                positions[hub.Name] = new Point(Width / 2.0, Height / 2.0);

            int n = Math.Max(1, outer.Count);

            // Ring always fills to the border — zones spread as far apart as possible.
            // The circle radius then shrinks only if needed to prevent overlap.
            // chord between adjacent zone centres = 2 · ringRadius · sin(π/n)
            // require chord ≥ 2 · zoneRadius + gap  →  zoneRadius ≤ (chord/2) - gap/2
            const double margin    = 18;                          // padding from image edge
            double ringRadius      = Width / 2.0 - margin;       // always maximum — zones spread as far apart as possible

            double chord           = 2.0 * ringRadius * Math.Sin(Math.PI / n);
            const double minGap    = 6;                           // minimum gap between circle edges
            double zoneRadius      = Math.Min(ZoneRadiusMax, (chord - minGap) / 2.0);

            // Ensure circle edges don't clip the image border
            ringRadius = Math.Min(ringRadius, Width / 2.0 - zoneRadius - margin);

            var center = new Point(Width / 2.0, Height / 2.0);

            for (int i = 0; i < outer.Count; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI * 2 / n;
                positions[outer[i].Name] = new Point(
                    center.X + Math.Cos(angle) * ringRadius,
                    center.Y + Math.Sin(angle) * ringRadius);
            }

            // Store for use during drawing
            _zoneRadius = zoneRadius;
            return positions;
        }

        private static bool TryLayoutBinaryTree(
            List<Zone> zones,
            List<Connection> connections,
            string? zeroAngleZone,
            out Dictionary<string, Point>? positions)
        {
            positions = null;
            var zoneNames = zones.Select(zone => zone.Name).ToHashSet(StringComparer.Ordinal);
            if (zoneNames.Count == 0)
                return false;

            var adjacency = zoneNames.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
            foreach (Connection connection in connections)
            {
                if (!string.Equals(connection.ConnectionType, "Direct", StringComparison.Ordinal))
                    continue;
                if (!zoneNames.Contains(connection.From) || !zoneNames.Contains(connection.To))
                    continue;

                adjacency[connection.From].Add(connection.To);
                adjacency[connection.To].Add(connection.From);
            }

            var directEdges = new HashSet<(string A, string B)>();
            foreach ((string node, List<string> neighbors) in adjacency)
            {
                foreach (string neighbor in neighbors)
                {
                    (string A, string B) normalized = string.CompareOrdinal(node, neighbor) <= 0
                        ? (node, neighbor)
                        : (neighbor, node);
                    directEdges.Add(normalized);
                }
            }

            int spawnCount = zoneNames.Count(name => name.StartsWith("Spawn-", StringComparison.Ordinal));
            int neutralCount = zoneNames.Count(name => name.StartsWith("Neutral-", StringComparison.Ordinal));
            bool allSpawnsAreLeaves = zoneNames
                .Where(name => name.StartsWith("Spawn-", StringComparison.Ordinal))
                .All(name => adjacency[name].Count == 1);
            bool isConnectedTree = directEdges.Count == zoneNames.Count - 1 && IsConnected(zoneNames, adjacency);
            bool hasBinaryBranching = zoneNames
                .Where(name => name.StartsWith("Neutral-", StringComparison.Ordinal))
                .Any(name => adjacency[name].Count >= 2);

            if (spawnCount < 2 || neutralCount == 0 || !allSpawnsAreLeaves || !isConnectedTree || !hasBinaryBranching)
                return false;

            string root = SelectBinaryTreeRoot(zoneNames, adjacency, zeroAngleZone);
            var parentByNode = new Dictionary<string, string?>(StringComparer.Ordinal);
            var depthByNode = new Dictionary<string, int>(StringComparer.Ordinal);
            var bfs = new Queue<string>();
            bfs.Enqueue(root);
            parentByNode[root] = null;
            depthByNode[root] = 0;
            while (bfs.Count > 0)
            {
                string current = bfs.Dequeue();
                foreach (string child in OrderNodesForStableTree(adjacency[current]))
                {
                    if (depthByNode.ContainsKey(child))
                        continue;
                    parentByNode[child] = current;
                    depthByNode[child] = depthByNode[current] + 1;
                    bfs.Enqueue(child);
                }
            }

            if (depthByNode.Count != zoneNames.Count)
                return false;

            var childrenByNode = zoneNames.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
            foreach ((string node, string? parent) in parentByNode)
            {
                if (parent is not null)
                    childrenByNode[parent].Add(node);
            }

            var signatureMemo = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string node in zoneNames)
            {
                childrenByNode[node] = childrenByNode[node]
                    .OrderBy(child => BuildSubtreeSignature(child, childrenByNode, signatureMemo), StringComparer.Ordinal)
                    .ToList();
            }

            var leafMemo = new Dictionary<string, int>(StringComparer.Ordinal);
            int totalLeafWeight = CountSubtreeLeaves(root, childrenByNode, leafMemo);
            if (totalLeafWeight <= 0)
                return false;

            int maxDepth = depthByNode.Values.DefaultIfEmpty(0).Max();
            double availableRadius = Width / 2.0 - ZoneRadiusMax - 18;
            double depthStep = maxDepth == 0
                ? 0
                : Math.Max((availableRadius - BinaryTreeInnerRadius) / maxDepth, 44);

            var center = new Point(Width / 2.0, Height / 2.0);
            var radialPositions = new Dictionary<string, Point>(StringComparer.Ordinal)
            {
                [root] = center
            };

            AssignSubtreePositions(root, -Math.PI / 2, 3 * Math.PI / 2, depthByNode, childrenByNode, leafMemo, center, depthStep, radialPositions);
            _zoneRadius = ComputeBinaryTreeZoneRadius(maxDepth, depthStep, depthByNode, radialPositions);
            positions = radialPositions;
            return true;
        }

        private static bool IsConnected(HashSet<string> zoneNames, Dictionary<string, List<string>> adjacency)
        {
            string start = zoneNames.First();
            var seen = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string neighbor in adjacency[current])
                {
                    if (!seen.Add(neighbor))
                        continue;
                    queue.Enqueue(neighbor);
                }
            }

            return seen.Count == zoneNames.Count;
        }

        private static string SelectBinaryTreeRoot(HashSet<string> zoneNames, Dictionary<string, List<string>> adjacency, string? zeroAngleZone)
        {
            if (!string.IsNullOrWhiteSpace(zeroAngleZone)
                && zoneNames.Contains(zeroAngleZone)
                && zeroAngleZone.StartsWith("Neutral-", StringComparison.Ordinal))
            {
                return zeroAngleZone;
            }

            var neutralCandidates = zoneNames
                .Where(name => name.StartsWith("Neutral-", StringComparison.Ordinal))
                .ToList();
            if (neutralCandidates.Count == 0)
                return zoneNames.First();

            int Eccentricity(string node)
            {
                var distances = ComputeDistances(node, adjacency);
                return distances.Values.DefaultIfEmpty(0).Max();
            }

            return neutralCandidates
                .OrderBy(Eccentricity)
                .ThenBy(node => adjacency[node].Count)
                .ThenBy(node => node, StringComparer.Ordinal)
                .First();
        }

        private static Dictionary<string, int> ComputeDistances(string start, Dictionary<string, List<string>> adjacency)
        {
            var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [start] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string neighbor in adjacency[current])
                {
                    if (distances.ContainsKey(neighbor))
                        continue;
                    distances[neighbor] = distances[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }

            return distances;
        }

        private static List<string> OrderNodesForStableTree(IEnumerable<string> nodes) =>
            nodes
                .OrderBy(node => node.StartsWith("Neutral-", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(node => node, StringComparer.Ordinal)
                .ToList();

        private static string BuildSubtreeSignature(
            string node,
            Dictionary<string, List<string>> childrenByNode,
            Dictionary<string, string> signatureMemo)
        {
            if (signatureMemo.TryGetValue(node, out string? cached))
                return cached;

            List<string> childSignatures = childrenByNode[node]
                .Select(child => BuildSubtreeSignature(child, childrenByNode, signatureMemo))
                .OrderBy(signature => signature, StringComparer.Ordinal)
                .ToList();

            string signature = $"{node}[{string.Join("|", childSignatures)}]";
            signatureMemo[node] = signature;
            return signature;
        }

        private static int CountSubtreeLeaves(
            string node,
            Dictionary<string, List<string>> childrenByNode,
            Dictionary<string, int> leafMemo)
        {
            if (leafMemo.TryGetValue(node, out int cached))
                return cached;
            if (childrenByNode[node].Count == 0)
                return leafMemo[node] = 1;

            int leaves = 0;
            foreach (string child in childrenByNode[node])
                leaves += CountSubtreeLeaves(child, childrenByNode, leafMemo);

            leafMemo[node] = leaves;
            return leaves;
        }

        private static void AssignSubtreePositions(
            string node,
            double startAngle,
            double endAngle,
            Dictionary<string, int> depthByNode,
            Dictionary<string, List<string>> childrenByNode,
            Dictionary<string, int> leafMemo,
            Point center,
            double depthStep,
            Dictionary<string, Point> positions)
        {
            List<string> children = childrenByNode[node];
            if (children.Count == 0)
                return;

            double span = endAngle - startAngle;
            int totalLeaves = children.Sum(child => leafMemo[child]);
            if (totalLeaves <= 0)
                return;

            double cursor = startAngle;
            foreach (string child in children)
            {
                double fraction = (double)leafMemo[child] / totalLeaves;
                double childStart = cursor;
                double childEnd = cursor + span * fraction;
                double angle = (childStart + childEnd) / 2.0;
                int depth = depthByNode[child];
                double radius = BinaryTreeInnerRadius + depth * depthStep;

                positions[child] = new Point(
                    center.X + Math.Cos(angle) * radius,
                    center.Y + Math.Sin(angle) * radius);

                AssignSubtreePositions(child, childStart, childEnd, depthByNode, childrenByNode, leafMemo, center, depthStep, positions);
                cursor = childEnd;
            }
        }

        private static double ComputeBinaryTreeZoneRadius(
            int maxDepth,
            double depthStep,
            Dictionary<string, int> depthByNode,
            Dictionary<string, Point> positions)
        {
            const double minGap = 8;
            double radialBound = maxDepth == 0
                ? ZoneRadiusMax
                : Math.Max(14, depthStep / 2.0 - minGap / 2.0);
            double zoneRadius = Math.Min(ZoneRadiusMax, radialBound);

            foreach (IGrouping<int, string> level in depthByNode.GroupBy(pair => pair.Value, pair => pair.Key))
            {
                List<string> nodes = level.ToList();
                if (nodes.Count < 2)
                    continue;

                double minDistance = double.MaxValue;
                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        Point a = positions[nodes[i]];
                        Point b = positions[nodes[j]];
                        double dx = a.X - b.X;
                        double dy = a.Y - b.Y;
                        minDistance = Math.Min(minDistance, Math.Sqrt(dx * dx + dy * dy));
                    }
                }

                if (double.IsFinite(minDistance))
                    zoneRadius = Math.Min(zoneRadius, Math.Max(14, (minDistance - minGap) / 2.0));
            }

            return Math.Clamp(zoneRadius, 14, ZoneRadiusMax);
        }

        // Per-layout computed zone radius (set by LayoutZones, used by DrawZone)
        [ThreadStatic] private static double _zoneRadius;

        // ── Connections ──────────────────────────────────────────────────────────

        private static void DrawConnections(DrawingContext dc, List<Connection> connections, Dictionary<string, Point> positions)
        {
            foreach (Connection conn in connections)
            {
                if (!positions.TryGetValue(conn.From, out Point from)) continue;
                if (!positions.TryGetValue(conn.To,   out Point to))   continue;

                bool isPortal = string.Equals(conn.ConnectionType, "Portal", StringComparison.Ordinal);

                // Skip Proximity lines entirely — only Direct/Default/Portal are meaningful visually
                if (string.Equals(conn.ConnectionType, "Proximity", StringComparison.Ordinal)) continue;

                Pen pen = isPortal
                    ? new Pen(new SolidColorBrush(PortalLineColor), 2)
                    : new Pen(new SolidColorBrush(DirectLineColor), 3);

                dc.DrawLine(pen, from, to);
            }
        }

        // ── Zone drawing ─────────────────────────────────────────────────────────

        private static void DrawZone(DrawingContext dc, Zone zone, Point pt)
        {
            bool isSpawn    = zone.Name.StartsWith("Spawn-",   StringComparison.Ordinal);
            bool isHub      = string.Equals(zone.Name, "Hub",  StringComparison.Ordinal)
                           || zone.Name.StartsWith("Hub-",     StringComparison.Ordinal);
            bool isNeutral  = zone.Name.StartsWith("Neutral-", StringComparison.Ordinal);
            bool isHoldCity = IsHoldCityZone(zone);
            int  castles    = CastleCount(zone);

            Brush fillBrush;
            Pen   outlinePen;

            if (isNeutral)
            {
                (fillBrush, outlinePen) = NeutralTierStyle(zone);
            }
            else if (isHub)
            {
                fillBrush  = new SolidColorBrush(HubFill);
                outlinePen = new Pen(new SolidColorBrush(HubBorder), 2);
            }
            else // player spawn
            {
                fillBrush  = new SolidColorBrush(SpawnFill);
                outlinePen = new Pen(new SolidColorBrush(SpawnBorder), 2.5);
            }

            // Hold-city zones get a bright golden outline on top of the normal one
            if (isHoldCity)
                outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(255, 215, 0)), 3.5);

            dc.DrawEllipse(fillBrush, outlinePen, pt, _zoneRadius, _zoneRadius);

            if (isHoldCity)
            {
                DrawHoldCityIcon(dc, pt, _zoneRadius);
            }
            else if (isSpawn)
            {
                DrawPlayerNumber(dc, zone, pt, _zoneRadius);
                if (castles > 1)
                    DrawCastleBadge(dc, pt, _zoneRadius, castles);
            }
            else if (isNeutral)
            {
                if (castles > 0)
                    DrawNeutralCastleContent(dc, pt, castles);
            }
            else if (isHub)
            {
                DrawText(dc, "Hub", pt, 32, Brushes.White, centered: true);
            }
        }

        // ── Hold-city detection ──────────────────────────────────────────────────

        private static bool IsHoldCityZone(Zone zone) =>
            zone.MainObjects?.Any(o => o.HoldCityWinCon == true) == true;

        // ── Hold-city icon (big golden house) ────────────────────────────────────
        // Drawn centred in the zone circle; a star/crown badge marks it as the target.

        private static void DrawHoldCityIcon(DrawingContext dc, Point centre, double r)
        {
            // Big golden house
            double iconSize = r * 1.35;
            var goldBrush   = new SolidColorBrush(Color.FromRgb(255, 215, 0));
            DrawHouseIcon(dc, centre, iconSize, goldBrush);

            // Small golden star badge at top-right of the circle
            double bx = centre.X + r * 0.62;
            double by = centre.Y - r * 0.62;
            double br = r * 0.30;
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(80, 60, 0)),
                new Pen(goldBrush, 1.2),
                new Point(bx, by), br, br);
            DrawText(dc, "★", new Point(bx, by), br * 1.55, goldBrush, centered: true, FontWeights.Bold);
        }

        // ── Castle badge (player zones) ──────────────────────────────────────────
        // Small filled circle at bottom-right edge of the zone circle, showing the count.

        private static void DrawCastleBadge(DrawingContext dc, Point zoneCentre, double r, int castles)
        {
            // Position: bottom-right quadrant, just on the border of the zone circle
            double bx = zoneCentre.X + r * 0.72;
            double by = zoneCentre.Y + r * 0.72;
            double br = r * 0.70;   // larger badge

            var badgeBg  = new SolidColorBrush(Color.FromRgb(28, 60, 35));
            var badgePen = new Pen(new SolidColorBrush(SpawnBorder), 1.5);
            dc.DrawEllipse(badgeBg, badgePen, new Point(bx, by), br, br);

            double iconSize = br * 0.60;
            double fontSize = br * 1.05;  // bigger font

            DrawHouseIcon(dc, new Point(bx - br * 0.32, by + 0.5), iconSize,
                new SolidColorBrush(Color.FromRgb(160, 230, 170)));

            DrawText(dc, castles.ToString(CultureInfo.InvariantCulture),
                new Point(bx + br * 0.40, by + 0.5), fontSize,
                new SolidColorBrush(Color.FromRgb(200, 245, 210)),
                centered: true, FontWeights.Bold);
        }

        // ── Neutral castle content (house icon + number centred in circle) ────────

        private static void DrawNeutralCastleContent(DrawingContext dc, Point pt, int castles)
        {
            string countStr = castles.ToString(CultureInfo.InvariantCulture);

            // Scale icon and font relative to current zone radius so they fit at any size
            double iconW   = _zoneRadius * 0.55;
            double fontSize = _zoneRadius * 0.62;
            double gap     = _zoneRadius * 0.12;

            double textW  = MeasureTextWidth(countStr, fontSize);
            double totalW = iconW + gap + textW;

            double startX = pt.X - totalW / 2;

            // House icon
            DrawHouseIcon(dc, new Point(startX + iconW / 2, pt.Y + 0.5), iconW,
                new SolidColorBrush(Color.FromRgb(220, 220, 200)));

            // Count number
            DrawText(dc, countStr,
                new Point(startX + iconW + gap + textW / 2, pt.Y + 0.5),
                fontSize, Brushes.White, centered: true, FontWeights.Bold);
        }

        // ── House icon ───────────────────────────────────────────────────────────
        // Simple roof (triangle) + body (rectangle) drawn with StreamGeometry.
        // `centre` is the horizontal+vertical midpoint of the icon bounding box.
        // `size` is the total height of the icon.

        private static void DrawHouseIcon(DrawingContext dc, Point centre, double size, Brush brush)
        {
            double w  = size * 0.9;   // width of house body
            double h  = size;         // total height
            double rh = h * 0.45;     // roof height
            double bh = h - rh;       // body height

            double left   = centre.X - w / 2;
            double right  = centre.X + w / 2;
            double top    = centre.Y - h / 2;
            double roofBt = top + rh;
            double bottom = top + h;

            // Roof triangle
            var roof = new StreamGeometry();
            using (var ctx = roof.Open())
            {
                ctx.BeginFigure(new Point(centre.X, top), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(right + w * 0.1, roofBt), isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(left  - w * 0.1, roofBt), isStroked: false, isSmoothJoin: false);
            }
            roof.Freeze();
            dc.DrawGeometry(brush, null, roof);

            // Body rectangle
            dc.DrawRectangle(brush, null, new Rect(left, roofBt, w, bh));
        }

        // ── Neutral tier styles ──────────────────────────────────────────────────

        private static (Brush Fill, Pen Outline) NeutralTierStyle(Zone zone)
        {
            // Derive tier from the guarded content pool names, which encode the tier number
            // directly (e.g. "classic_template_pool_random_t4_item") and are never scaled.
            //   t4 or t5 → Gold  (High)
            //   t2       → Bronze (Low)
            //   anything else / t3 → Silver (Medium)
            var pool = zone.GuardedContentPool?.FirstOrDefault() ?? string.Empty;
            if (pool.Contains("_t4_") || pool.Contains("_t5_"))
                return (new SolidColorBrush(GoldFill),   new Pen(new SolidColorBrush(GoldBorder),   2.5));
            if (pool.Contains("_t2_") || pool.Contains("_t1_"))
                return (new SolidColorBrush(BronzeFill), new Pen(new SolidColorBrush(BronzeBorder), 2.5));
            return     (new SolidColorBrush(SilverFill), new Pen(new SolidColorBrush(SilverBorder), 2.5));
        }

        // ── Player number ────────────────────────────────────────────────────────
        // Shows the player number (1–8) read from the MainObject "spawn" field ("Player1" → "1").

        private static void DrawPlayerNumber(DrawingContext dc, Zone zone, Point centre, double r)
        {
            string label = "?";
            // Find the Spawn main object and parse its "spawn" value, e.g. "Player3" → "3"
            string? spawnValue = zone.MainObjects?
                .FirstOrDefault(o => o.Type == "Spawn")?.Spawn;
            if (spawnValue is not null && spawnValue.StartsWith("Player", StringComparison.Ordinal))
            {
                string number = spawnValue["Player".Length..];
                if (int.TryParse(number, out _))
                    label = number;
            }

            var brush = new SolidColorBrush(Color.FromRgb(160, 230, 170));
            DrawText(dc, label, centre, r * 1.05, brush, centered: true, FontWeights.Bold);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static int CastleCount(Zone zone)
        {
            int count = 0;
            foreach (MainObject obj in zone.MainObjects ?? [])
                if (obj.Type is "City" or "Spawn")
                    count++;
            return count;
        }

        private static void DrawText(DrawingContext dc, string text, Point point, double size,
            Brush brush, bool centered, FontWeight? weight = null)
        {
            var ft = MakeFormattedText(text, size, brush, weight);
            var origin = centered
                ? new Point(point.X - ft.Width / 2, point.Y - ft.Height / 2)
                : point;
            dc.DrawText(ft, origin);
        }

        private static FormattedText MakeFormattedText(string text, double size, Brush brush, FontWeight? weight = null)
            => new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"),
                    FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
                size, brush, 1.0);

        private static double MeasureTextWidth(string text, double size)
            => MakeFormattedText(text, size, Brushes.White).Width;
    }
}
