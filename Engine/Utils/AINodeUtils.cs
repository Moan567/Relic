using Microsoft.Xna.Framework;
using Rockwall;
using System;
using System.Collections.Generic;

namespace Engine.Utils
{
    public class AINodeUtils
    {
        static NodeGraph graph => GlobalMapData.ActiveMap.Nodegraph;

        private const float FloorScanDepth = 0.25f;

        private const float StepHeight = 0.25f;

        private const float WallRayHeight = 0.5f;

        public static int FindClosestNode(Vector3 p)
        {
            float closestDist = float.MaxValue;
            float closestDistNoLOS = float.MaxValue;
            int targNode = 0;
            int fallbackNode = 0;

            var hit = BSPRoot.TraceRay(new Ray(p, Vector3.Down), 100f, false);
            if (hit.Hit) p = hit.Point;

            for (int i = 0; i < graph.Nodes.Length; i++)
            {
                Vector3 nodepos = graph.Nodes[i].Position;
                float dist = Vector3.Distance(nodepos, p);

                // Always track the closest by raw distance as a fallback
                if (dist < closestDistNoLOS)
                {
                    closestDistNoLOS = dist;
                    fallbackNode = i;
                }

                hit = BSPRoot.TraceRay(new Ray(nodepos + Vector3.Up * 0.02f, Vector3.Normalize(p - nodepos)), dist);
                if (dist < closestDist && !hit.Hit)
                {
                    closestDist = dist;
                    targNode = i;
                }
            }
            return closestDist < float.MaxValue ? targNode : fallbackNode;
        }

        public static Vector3 ProjectOnFloor(Vector3 a, WorldEntity self)
        {
            bool hit = Collision.CastPhysicsWorld(new Ray(a, Vector3.Down), 100f, out var result, self.PhysicsBodyID);
            var point = a + Vector3.Down * result.Fraction * 100f;
            return hit ? point : a;
        }

        /// <summary>
        /// Returns true if there are no walls between a and b at standing height.
        /// </summary>
        public static bool HasWallClearance(Vector3 a, Vector3 b, float? height = null)
        {
            Vector3 dir = b - a;
            dir.Y = 0;
            float dist = dir.Length();
            if (dist < 0.001f) return true;
            dir /= dist;

            var hit = BSPRoot.TraceRay(new Ray(a + Vector3.Up * (height??WallRayHeight), dir), dist);
            return !hit.Hit;
        }

        /// <summary>
        /// Returns true if the straight-line path from a to b is walkable.
        /// </summary>
        public static bool IsPathWalkable(Vector3 a, Vector3 b, out Vector3 failurePoint)
        {
            if (!HasWallClearance(a, b))
            {
                failurePoint = b;
                return false;
            }

            float distToEnd = Vector3.Distance(a, b);
            int stepsToCheck = Math.Max((int)MathF.Round(distToEnd * 8), 1);

            for (int i = 0; i <= stepsToCheck; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)stepsToCheck);
                if (!TestPointWalkable(p) && !TestPointWalkable(p + Vector3.Up * StepHeight))
                {
                    failurePoint = p;
                    return false;
                }
            }

            failurePoint = Vector3.Zero;
            return true;
        }

        public static bool TestStepWalkable(Vector3 dir, ref WorldEntity entity, out float stepDistance)
        {
            stepDistance = 0.25f;
            bool result = false;
            entity.Position += dir * 0.25f + Vector3.Up * 0.01f;

            while (!result && stepDistance < 1)
            {
                entity.Position += dir * 0.25f;
                result = Collision.CheckBounds(entity);
                stepDistance += 0.25f;
            }

            entity.Position -= dir * stepDistance + Vector3.Up * 0.01f;
            return result;
        }

        static bool TestPointWalkable(Vector3 p)
        {
            var hit = BSPRoot.TraceRay(new Ray(p + Vector3.Up * 0.01f, Vector3.Down), FloorScanDepth);
            return hit.Hit;
        }

        public static int[] Search(int startNode, int endNode)
        {
            int nodeCount = graph.Nodes.Length;

            if (nodeCount == 0) return new[] { startNode, endNode };

            int[] gCosts = new int[nodeCount];
            int[] hCosts = new int[nodeCount];
            int[] parents = new int[nodeCount];

            const int Unvisited = int.MaxValue;
            for (int i = 0; i < nodeCount; i++) gCosts[i] = Unvisited;
            gCosts[startNode] = 0;

            List<int> open = new List<int>();
            HashSet<int> closed = new HashSet<int>();
            open.Add(startNode);

            while (open.Count > 0)
            {
                int node = open[0];
                for (int i = 1; i < open.Count; i++)
                {
                    int fi = gCosts[open[i]] + hCosts[open[i]];
                    int fn = gCosts[node] + hCosts[node];
                    if (fi <= fn && hCosts[open[i]] < hCosts[node])
                        node = open[i];
                }

                open.Remove(node);
                closed.Add(node);

                if (node == endNode)
                {
                    List<int> path = new List<int>();
                    int searchNode = endNode;
                    while (searchNode != startNode)
                    {
                        path.Add(searchNode);
                        searchNode = parents[searchNode];
                    }
                    path.Reverse();
                    return [.. path];
                }

                for (int i = 0; i < graph.Nodes[node].Connections.Length; i++)
                {
                    int neighbor = graph.Nodes[node].Connections[i];
                    if (closed.Contains(neighbor)) continue;

                    int newG = (int)Vector3.DistanceSquared(graph.Nodes[node].Position, graph.Nodes[neighbor].Position) + gCosts[node];

                    if (newG < gCosts[neighbor] || !open.Contains(neighbor))
                    {
                        gCosts[neighbor] = newG;
                        hCosts[neighbor] = (int)Vector3.DistanceSquared(graph.Nodes[endNode].Position, graph.Nodes[neighbor].Position);
                        parents[neighbor] = node;
                        if (!open.Contains(neighbor)) open.Add(neighbor);
                    }
                }
            }

            return [];
        }
    }
}