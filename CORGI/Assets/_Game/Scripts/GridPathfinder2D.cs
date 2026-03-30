using System.Collections.Generic;
using UnityEngine;

namespace _Game.Scripts
{
    public static class GridPathfinder2D
    {
        public struct Settings
        {
            public Grid Grid;
            public Vector2 FallbackOrigin;
            public Vector2 FallbackCellSize;
            public LayerMask ObstacleLayers;
            public Vector2 AgentSize;
            public float Clearance;
            public int SearchPaddingCells;
            public int MaxIterations;
            public int GoalSearchRadius;
            public Collider2D IgnoredCollider;
            public Rigidbody2D IgnoredRigidbody;
            public Transform IgnoredRoot;
            public Transform IgnoredSecondaryRoot;
            public Collider2D[] OverlapBuffer;
        }

        private static readonly Vector2Int[] NeighborOffsets =
        {
            new(0, 1),
            new(1, 0),
            new(0, -1),
            new(-1, 0),
            new(1, 1),
            new(1, -1),
            new(-1, -1),
            new(-1, 1)
        };

        private sealed class NodeRecord
        {
            public Vector2Int Cell;
            public Vector2Int Parent;
            public int GCost = int.MaxValue;
            public int HCost;
            public bool HasParent;
            public bool Closed;

            public int FCost => GCost + HCost;
        }

        public static bool TryFindPath(Vector2 startWorldPosition, Vector2 goalWorldPosition, Settings settings, List<Vector2> results)
        {
            results?.Clear();

            if (results == null || settings.OverlapBuffer == null || settings.OverlapBuffer.Length == 0)
            {
                return false;
            }

            var cellSize = GetCellSize(settings);
            if (cellSize.x <= 0.001f || cellSize.y <= 0.001f)
            {
                return false;
            }

            var startCell = WorldToCell(startWorldPosition, settings);
            var desiredGoalCell = WorldToCell(goalWorldPosition, settings);
            var bounds = BuildSearchBounds(startCell, desiredGoalCell, Mathf.Max(settings.SearchPaddingCells, 1));
            var goalCell = FindNearestWalkableCell(desiredGoalCell, startCell, bounds, settings);
            if (goalCell == startCell)
            {
                return true;
            }

            var openSet = new List<NodeRecord>(64);
            var records = new Dictionary<Vector2Int, NodeRecord>(128);
            var startRecord = GetOrCreateRecord(startCell, desiredGoalCell, records);
            startRecord.Cell = startCell;
            startRecord.GCost = 0;
            startRecord.HCost = Heuristic(startCell, goalCell);
            startRecord.HasParent = false;
            openSet.Add(startRecord);

            var iterations = 0;
            while (openSet.Count > 0 && iterations < Mathf.Max(settings.MaxIterations, 32))
            {
                iterations++;
                var current = ExtractBestNode(openSet);
                if (current == null)
                {
                    break;
                }

                if (current.Cell == goalCell)
                {
                    ReconstructPath(current, records, settings, results);
                    SimplifyPath(startWorldPosition, goalWorldPosition, settings, results);
                    return true;
                }

                current.Closed = true;

                for (var i = 0; i < NeighborOffsets.Length; i++)
                {
                    var offset = NeighborOffsets[i];
                    var neighborCell = current.Cell + offset;
                    if (!Contains(bounds, neighborCell))
                    {
                        continue;
                    }

                    var isDiagonal = offset.x != 0 && offset.y != 0;
                    if (isDiagonal)
                    {
                        var horizontalCell = current.Cell + new Vector2Int(offset.x, 0);
                        var verticalCell = current.Cell + new Vector2Int(0, offset.y);
                        if (!IsCellWalkable(horizontalCell, startCell, bounds, settings) || !IsCellWalkable(verticalCell, startCell, bounds, settings))
                        {
                            continue;
                        }
                    }

                    if (!IsCellWalkable(neighborCell, startCell, bounds, settings))
                    {
                        continue;
                    }

                    var stepCost = isDiagonal ? 14 : 10;
                    var tentativeCost = current.GCost + stepCost;
                    var neighbor = GetOrCreateRecord(neighborCell, goalCell, records);
                    if (neighbor.Closed && tentativeCost >= neighbor.GCost)
                    {
                        continue;
                    }

                    if (tentativeCost >= neighbor.GCost)
                    {
                        continue;
                    }

                    neighbor.Cell = neighborCell;
                    neighbor.GCost = tentativeCost;
                    neighbor.HCost = Heuristic(neighborCell, goalCell);
                    neighbor.Parent = current.Cell;
                    neighbor.HasParent = true;
                    neighbor.Closed = false;

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                }
            }

            return false;
        }

        private static Vector2Int FindNearestWalkableCell(Vector2Int desiredGoalCell, Vector2Int startCell, RectInt bounds, Settings settings)
        {
            if (IsCellWalkable(desiredGoalCell, startCell, bounds, settings))
            {
                return desiredGoalCell;
            }

            var bestCell = desiredGoalCell;
            var bestScore = int.MaxValue;
            var radiusLimit = Mathf.Max(settings.GoalSearchRadius, 1);

            for (var radius = 1; radius <= radiusLimit; radius++)
            {
                var foundAtRadius = false;
                for (var x = desiredGoalCell.x - radius; x <= desiredGoalCell.x + radius; x++)
                {
                    for (var y = desiredGoalCell.y - radius; y <= desiredGoalCell.y + radius; y++)
                    {
                        var isPerimeter = x == desiredGoalCell.x - radius || x == desiredGoalCell.x + radius || y == desiredGoalCell.y - radius || y == desiredGoalCell.y + radius;
                        if (!isPerimeter)
                        {
                            continue;
                        }

                        var candidate = new Vector2Int(x, y);
                        if (!Contains(bounds, candidate) || !IsCellWalkable(candidate, startCell, bounds, settings))
                        {
                            continue;
                        }

                        var score = Heuristic(candidate, desiredGoalCell) + Heuristic(candidate, startCell);
                        if (score >= bestScore)
                        {
                            continue;
                        }

                        bestScore = score;
                        bestCell = candidate;
                        foundAtRadius = true;
                    }
                }

                if (foundAtRadius)
                {
                    return bestCell;
                }
            }

            return startCell;
        }

        private static bool IsCellWalkable(Vector2Int cell, Vector2Int startCell, RectInt bounds, Settings settings)
        {
            if (cell == startCell)
            {
                return true;
            }

            if (!Contains(bounds, cell))
            {
                return false;
            }

            var filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = settings.ObstacleLayers,
                useTriggers = false
            };

            var center = CellToWorld(cell, settings);
            var size = GetAgentProbeSize(settings);
            var hitCount = Physics2D.OverlapBox(center, size, 0f, filter, settings.OverlapBuffer);
            for (var i = 0; i < hitCount; i++)
            {
                var collider = settings.OverlapBuffer[i];
                if (IsIgnoredCollider(collider, settings))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void ReconstructPath(NodeRecord current, IReadOnlyDictionary<Vector2Int, NodeRecord> records, Settings settings, List<Vector2> results)
        {
            var reversedPath = new List<Vector2>(32);
            var cursor = current;
            reversedPath.Add(CellToWorld(cursor.Cell, settings));

            while (cursor.HasParent && records.TryGetValue(cursor.Parent, out var parent))
            {
                cursor = parent;
                reversedPath.Add(CellToWorld(cursor.Cell, settings));
            }

            for (var i = reversedPath.Count - 1; i >= 0; i--)
            {
                results.Add(reversedPath[i]);
            }

            if (results.Count > 0)
            {
                results.RemoveAt(0);
            }
        }

        private static void SimplifyPath(Vector2 startWorldPosition, Vector2 goalWorldPosition, Settings settings, List<Vector2> results)
        {
            if (results.Count == 0)
            {
                return;
            }

            if (HasClearTravelPath(startWorldPosition, goalWorldPosition, settings))
            {
                results.Clear();
                return;
            }

            var simplified = new List<Vector2>(results.Count);
            var anchor = startWorldPosition;
            for (var i = 0; i < results.Count; i++)
            {
                var nextTarget = i == results.Count - 1 ? goalWorldPosition : results[i + 1];
                if (HasClearTravelPath(anchor, nextTarget, settings))
                {
                    continue;
                }

                anchor = results[i];
                simplified.Add(anchor);
            }

            results.Clear();
            results.AddRange(simplified);
        }

        private static bool HasClearTravelPath(Vector2 from, Vector2 to, Settings settings)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            if (distance <= 0.05f)
            {
                return true;
            }

            if (settings.IgnoredCollider == null)
            {
                return true;
            }

            var filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = settings.ObstacleLayers,
                useTriggers = false
            };

            var hitBuffer = new RaycastHit2D[12];
            var hitCount = settings.IgnoredCollider.Cast(delta / distance, filter, hitBuffer, distance + settings.Clearance);
            for (var i = 0; i < hitCount; i++)
            {
                var hit = hitBuffer[i];
                if (hit.collider == null || IsIgnoredCollider(hit.collider, settings))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsIgnoredCollider(Collider2D collider, Settings settings)
        {
            if (collider == null)
            {
                return true;
            }

            if (settings.IgnoredCollider != null && collider == settings.IgnoredCollider)
            {
                return true;
            }

            if (settings.IgnoredRigidbody != null && collider.attachedRigidbody == settings.IgnoredRigidbody)
            {
                return true;
            }

            var colliderTransform = collider.transform;
            if (settings.IgnoredRoot != null && (colliderTransform == settings.IgnoredRoot || colliderTransform.IsChildOf(settings.IgnoredRoot)))
            {
                return true;
            }

            if (settings.IgnoredSecondaryRoot != null && (colliderTransform == settings.IgnoredSecondaryRoot || colliderTransform.IsChildOf(settings.IgnoredSecondaryRoot)))
            {
                return true;
            }

            return false;
        }

        private static NodeRecord GetOrCreateRecord(Vector2Int cell, Vector2Int goalCell, IDictionary<Vector2Int, NodeRecord> records)
        {
            if (records.TryGetValue(cell, out var record))
            {
                return record;
            }

            record = new NodeRecord
            {
                Cell = cell,
                HCost = Heuristic(cell, goalCell)
            };
            records[cell] = record;
            return record;
        }

        private static NodeRecord ExtractBestNode(IList<NodeRecord> openSet)
        {
            if (openSet.Count == 0)
            {
                return null;
            }

            var bestIndex = 0;
            var bestNode = openSet[0];
            for (var i = 1; i < openSet.Count; i++)
            {
                var candidate = openSet[i];
                if (candidate.FCost < bestNode.FCost || (candidate.FCost == bestNode.FCost && candidate.HCost < bestNode.HCost))
                {
                    bestIndex = i;
                    bestNode = candidate;
                }
            }

            openSet.RemoveAt(bestIndex);
            return bestNode;
        }

        private static RectInt BuildSearchBounds(Vector2Int startCell, Vector2Int goalCell, int padding)
        {
            var minX = Mathf.Min(startCell.x, goalCell.x) - padding;
            var maxX = Mathf.Max(startCell.x, goalCell.x) + padding;
            var minY = Mathf.Min(startCell.y, goalCell.y) - padding;
            var maxY = Mathf.Max(startCell.y, goalCell.y) + padding;
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static bool Contains(RectInt rect, Vector2Int cell)
        {
            return cell.x >= rect.xMin && cell.x < rect.xMax && cell.y >= rect.yMin && cell.y < rect.yMax;
        }

        private static Vector2Int WorldToCell(Vector2 worldPosition, Settings settings)
        {
            if (settings.Grid != null)
            {
                var gridCell = settings.Grid.WorldToCell(worldPosition);
                return new Vector2Int(gridCell.x, gridCell.y);
            }

            var cellSize = GetCellSize(settings);
            return new Vector2Int(
                Mathf.FloorToInt((worldPosition.x - settings.FallbackOrigin.x) / cellSize.x),
                Mathf.FloorToInt((worldPosition.y - settings.FallbackOrigin.y) / cellSize.y));
        }

        private static Vector2 CellToWorld(Vector2Int cell, Settings settings)
        {
            if (settings.Grid != null)
            {
                var world = settings.Grid.GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0));
                return new Vector2(world.x, world.y);
            }

            var cellSize = GetCellSize(settings);
            return new Vector2(
                settings.FallbackOrigin.x + (cell.x + 0.5f) * cellSize.x,
                settings.FallbackOrigin.y + (cell.y + 0.5f) * cellSize.y);
        }

        private static Vector2 GetCellSize(Settings settings)
        {
            if (settings.Grid != null)
            {
                var cellSize = settings.Grid.cellSize;
                return new Vector2(Mathf.Abs(cellSize.x), Mathf.Abs(cellSize.y));
            }

            return new Vector2(
                Mathf.Max(Mathf.Abs(settings.FallbackCellSize.x), 0.01f),
                Mathf.Max(Mathf.Abs(settings.FallbackCellSize.y), 0.01f));
        }

        private static Vector2 GetAgentProbeSize(Settings settings)
        {
            var size = settings.AgentSize;
            if (size.x <= 0.001f || size.y <= 0.001f)
            {
                size = GetCellSize(settings) * 0.6f;
            }

            size += Vector2.one * Mathf.Max(settings.Clearance, 0f);
            return size;
        }

        private static int Heuristic(Vector2Int from, Vector2Int to)
        {
            var deltaX = Mathf.Abs(from.x - to.x);
            var deltaY = Mathf.Abs(from.y - to.y);
            return 10 * (deltaX + deltaY) + (14 - 20) * Mathf.Min(deltaX, deltaY);
        }
    }
}

