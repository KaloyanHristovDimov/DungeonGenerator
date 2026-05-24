using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Vector3Graph
{
    private Dictionary<Vector3, List<Vector3>> nodes;
    public Vector3Graph()
    {
        nodes = new Dictionary<Vector3, List<Vector3>>();
    }

    public void AddNode(Vector3 node)
    {
        if (!nodes.ContainsKey(node))
        {
            nodes.Add(node, new List<Vector3>());
        }
    }

    public void AddEdge(Vector3 fromNode, Vector3 toNode)
    {
        if (!nodes.ContainsKey(fromNode)) nodes.Add(fromNode, new List<Vector3>());
        if (!nodes.ContainsKey(toNode)) nodes.Add(toNode, new List<Vector3>());

        if (!nodes[fromNode].Contains(toNode))
            nodes[fromNode].Add(toNode);

        if (!nodes[toNode].Contains(fromNode))
            nodes[toNode].Add(fromNode);
    }

    public void RemoveNode(Vector3 node)
    {
        if (!nodes.ContainsKey(node))
            return;

        foreach (Vector3 neighbor in nodes[node])
        {
            nodes[neighbor].Remove(node);
        }

        nodes.Remove(node);
    }

    public bool CanRemoveNodeWithoutDisconnecting(Vector3 nodeToRemove, List<Vector3> requiredNodes)
    {
        Vector3 startNode = requiredNodes[0];

        if (startNode == nodeToRemove)
            return false;

        HashSet<Vector3> visited = new HashSet<Vector3>();
        Queue<Vector3> queue = new Queue<Vector3>();

        visited.Add(startNode);
        queue.Enqueue(startNode);

        while (queue.Count > 0)
        {
            Vector3 currentNode = queue.Dequeue();

            if (!nodes.ContainsKey(currentNode))
                continue;

            foreach (Vector3 neighbor in nodes[currentNode])
            {
                if (neighbor == nodeToRemove)
                    continue;

                if (visited.Contains(neighbor))
                    continue;

                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        foreach (Vector3 requiredNode in requiredNodes)
        {
            if (!visited.Contains(requiredNode))
                return false;
        }

        return true;
    }

    public List<Vector3> ListGraph()
    {
        List<Vector3> list = new List<Vector3>();
        foreach (var node in nodes)
        {
            if(!list.Contains(node.Key))
                list.Add(node.Key);
            foreach (var neighbor in node.Value)
            {
                if (!list.Contains(neighbor))
                    list.Add(neighbor);
            }
        }
        return list;
    }

    public void PrintGraph() 
    {
        foreach (var node in nodes) 
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                DebugExtension.DebugWireSphere(node.Key, Color.green, 0.5f);
            });

            foreach (var neighbor in node.Value)
            {
                DebugDrawingBatcher.GetInstance().BatchCall(() =>
                {
                    Debug.DrawLine(node.Key, neighbor, Color.green);
                });
            }
        }
    }

    public IEnumerator SlowPrintGraph() 
    {
        foreach (var node in nodes)
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                DebugExtension.DebugWireSphere(node.Key, Color.green, 0.5f);
            });

            yield return new WaitForSeconds(0.1f);

            foreach (var neighbor in node.Value)
            {
                DebugDrawingBatcher.GetInstance().BatchCall(() =>
                {
                    Debug.DrawLine(node.Key, neighbor, Color.green);
                });
                yield return new WaitForSeconds(0.1f);
            }
        }
    }
}