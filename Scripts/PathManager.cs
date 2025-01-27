using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace WarlordsOfArcania.GameSystems
{
    /// <summary>
    /// Manages the pathfinding graph constructed from splines and provides methods for pathfinding.
    /// </summary>
    public class PathManager : MonoBehaviour
    {

        public static PathManager Instance { get; private set; }

        /// <summary>
        /// The spline container holding all the splines used for pathfinding.
        /// </summary>
        public SplineContainer splineContainer;

        /// <summary>
        /// Dictionary mapping spline and knot indices to their corresponding graph nodes.
        /// </summary>
        public Dictionary<(int splineIndex, int knotIndex), GraphNode> graphNodes;

        private void Awake()
        {
            if(Instance != null)
            {
                Debug.LogError("Multiple PathManager instances found!");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            BuildGraph();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Builds the graph nodes and edges based on the splines in the spline container.
        /// </summary>
        private void BuildGraph()
        {
            graphNodes = new Dictionary<(int, int), GraphNode>();

            for(int splineIndex = 0; splineIndex < splineContainer.Splines.Count; splineIndex++) 
            {
                Spline spline = splineContainer.Splines[splineIndex];
                int knotCount = spline.Count;

                for(int knotIndex = 0; knotIndex < knotCount; knotIndex++)
                {
                    var nodeKey = (splineIndex, knotIndex);
                    if (!graphNodes.ContainsKey(nodeKey))
                    {
                        GraphNode node = new GraphNode
                        {
                            splineIndex = splineIndex,
                            knotIndex = knotIndex,
                            position = spline[knotIndex].Position,
                            edges = new List<GraphEdge>()
                        };
                        graphNodes[nodeKey] = node;
                    }

                }
            }

            // Create edges between consecutive knots on the same spline
            foreach (var node in graphNodes.Values)
            {
                var nextKnotIndex = node.knotIndex + 1;
                var nextNodeKey = (node.splineIndex, nextKnotIndex);
                if (graphNodes.ContainsKey(nextNodeKey))
                {
                    var nextNode = graphNodes[nextNodeKey];
                    float cost = Vector3.Distance(node.position, nextNode.position);
                    node.edges.Add(new GraphEdge { fromNode = node, toNode = nextNode, cost = cost });
                    nextNode.edges.Add(new GraphEdge { fromNode = nextNode, toNode = node, cost = cost });
                }
            }

            // Create edges between knots at the same position but on different splines
            foreach (var node in graphNodes.Values)
            {
                foreach (var otherNode in graphNodes.Values)
                {
                    if (node != otherNode && node.position == otherNode.position)
                    {
                        node.edges.Add(new GraphEdge { fromNode = node, toNode = otherNode, cost = 0f });
                    }
                }
            }
        }

        /// <summary>
        /// Finds the graph node closest to the given position.
        /// </summary>
        /// <param name="position">The position to find the closest node to.</param>
        /// <returns>The closest <see cref="GraphNode"/> to the given position.</returns>
        public GraphNode GetClosestNode(Vector3 position)
        {
            GraphNode closestNode = null;
            float minDistance = Mathf.Infinity;

            foreach (var node in graphNodes.Values)
            {
                float distance = Vector3.Distance(position, node.position);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestNode = node;
                }
            }

            return closestNode;
        }

        /// <summary>
        /// Finds the shortest path between the start node and goal node using Dijkstra's algorithm.
        /// </summary>
        /// <param name="startNode">The starting <see cref="GraphNode"/>.</param>
        /// <param name="goalNode">The goal <see cref="GraphNode"/>.</param>
        /// <returns>A list of <see cref="GraphNode"/> representing the path from start to goal.</returns>
        public List<GraphNode> FindPath(GraphNode startNode, GraphNode goalNode)
        {
            var frontier = new PriorityQueue<GraphNode>();
            var cameFrom = new Dictionary<GraphNode, GraphNode>();
            var costSoFar = new Dictionary<GraphNode, float>();

            frontier.Enqueue(startNode, 0);
            cameFrom[startNode] = null;
            costSoFar[startNode] = 0;

            while(frontier.Count > 0)
            {
                var current = frontier.Dequeue();

                if(current == goalNode)
                {
                    break;
                }

                foreach(var edge in current.edges)
                {
                    var next = edge.toNode;
                    float newCost = costSoFar[current] + edge.cost;

                    if(!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                    {
                        costSoFar[next] = newCost;
                        float priority = newCost;
                        frontier.Enqueue(next, priority);
                        cameFrom[next] = current;
                    }
                }
            }

            List<GraphNode> path = new List<GraphNode>();
            var node = goalNode;
            while(node != null)
            {
                path.Add(node);
                node = cameFrom.ContainsKey(node) ? cameFrom[node] : null;
            }
            path.Reverse();
            return path;
        }
    }

    /// <summary>
    /// Represents a node in the pathfinding graph, corresponding to a knot on a spline.
    /// </summary>
    public class GraphNode
    {
        public int splineIndex;
        public int knotIndex;
        public Vector3 position;
        public List<GraphEdge> edges;
    }

    /// <summary>
    /// Represents an edge in the pathfinding graph, connecting two nodes with an associated cost.
    /// </summary>
    public class GraphEdge
    {
        public GraphNode fromNode;
        public GraphNode toNode;
        public float cost;
    }

    /// <summary>
    /// A simple priority queue implementation for pathfinding algorithms.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the priority queue.</typeparam>
    public class PriorityQueue<T>
    {
        private List<(T item, float priority)> elements = new List<(T, float)>();

        /// <summary>
        /// Gets the number of elements in the priority queue.
        /// </summary>
        public int Count => elements.Count;

        /// <summary>
        /// Adds an item to the queue with the given priority.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        /// <param name="priority">The priority of the item.</param>
        public void Enqueue(T item, float priority)
        {
            elements.Add((item, priority));
        }

        /// <summary>
        /// Removes and returns the item with the lowest priority from the queue.
        /// </summary>
        /// <returns>The item with the lowest priority.</returns>
        public T Dequeue()
        {
            int bestIndex = 0;
            float bestPriority = elements[0].priority;

            for (int i = 1; i < elements.Count; i++)
            {
                if (elements[i].priority < bestPriority)
                {
                    bestPriority = elements[i].priority;
                    bestIndex = i;
                }
            }

            T bestItem = elements[bestIndex].item;
            elements.RemoveAt(bestIndex);
            return bestItem;
        }
    }
}

