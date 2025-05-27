using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBase : MonoBehaviour
{
    [Header("Characteristics")]
    public EnemyTypeEnum enemyTypeEnum;
    public bool isBlind = false;
    public bool isDeaf = false;
    public bool attractedByLight = false;

	[HideInInspector] public NavMeshAgent agent;

	public virtual float CalculatePathDistance(GameObject target)
    {
		if (agent == null) return -1;
		// Creates a new path object
		NavMeshPath path = new NavMeshPath();

		// Calculates the path from the enemy to the player
		if (agent.CalculatePath(target.transform.position, path))
		{
			float totalDistance = 0f;

			// Loop through each corner of the path and sum the distances
			for (int i = 1; i < path.corners.Length; i++)
			{
				totalDistance += Vector3.Distance(path.corners[i - 1], path.corners[i]);
			}

			return totalDistance;
		}

		// Return a very high value if no path is found
		return Mathf.Infinity;
	}
}

public enum EnemyTypeEnum {Unassigned, ASE, Stalker, Spider, Watcher}

public static class ExtensionMethods
{
	public static float GetPathRemainingDistance(this NavMeshAgent navMeshAgent)
	{
		if (navMeshAgent.pathPending ||
			navMeshAgent.pathStatus == NavMeshPathStatus.PathInvalid ||
			navMeshAgent.path.corners.Length == 0)
			return -1f;

		float distance = 0.0f;
		for (int i = 0; i < navMeshAgent.path.corners.Length - 1; ++i)
		{
			distance += Vector3.Distance(navMeshAgent.path.corners[i], navMeshAgent.path.corners[i + 1]);
		}

		return distance;
	}
}

