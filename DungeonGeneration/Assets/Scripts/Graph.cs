using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor.TerrainTools;
using UnityEngine;

public class Graph<T>
{
    protected Dictionary<T, List<T>> nodes;

    public Graph()
    {
        nodes = new Dictionary<T, List<T>>();
    }

    public void AddNode(T node)
    {
        if (!nodes.ContainsKey(node))
        {
            nodes.Add(node, new List<T>());
        }
    }

    public void AddEdge(T fromNode, T toNode)
    {
        if (!nodes.ContainsKey(fromNode)) nodes.Add(fromNode, new List<T>());
        if (!nodes.ContainsKey(toNode)) nodes.Add(toNode, new List<T>());

        if (!nodes[fromNode].Contains(toNode) && !nodes[toNode].Contains(fromNode))
        {
            nodes[fromNode].Add(toNode);
            nodes[toNode].Add(fromNode);
        }
    }
    public List<T> ListGraph()
    {
        List<T> list = new List<T>();
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
            Debug.Log("node");
            foreach (var neighbor in node.Value)
            {
                Debug.Log("neighbor");
            }
        }
    }
}