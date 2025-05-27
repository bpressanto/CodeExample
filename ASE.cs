using Ink.Parsed;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public class ASE : EnemyBase, IDataPersistence
{
	[Header("General Settings")]
	public ASEState state;
	
	[SerializeField] private float distanceToTarget = 0f; //Distance to patrolling points or any source of noise within hearing distance
	public float distanceToPlayer;
	[SerializeField] private Transform noiseTarget; //Either the patrolling points, the player or whatever object made noise
	private GameObject currentTarget;
	private GameObject soundSource;
	private string enemyType;
	[HideInInspector] public float distanceFromPlayer;

	[Header("Patrolling Settings")]
	public float speedPatrolling = 1f;
	private List<GameObject> patrollingPoints = new List<GameObject>();
	public int secondsAtEachPoint = 8;
	private int saveSecondsAtEachPoint;
	//private GameObject currentPoint;
	public float stoppingDistance = 0.1f;
	private int pointIndex = 0;
	private bool reachedDestination = false;

	[Header("Engaging Settings")]
	public float speedEngaging = 5f;
	public float noiseRange = 10f;
	public float hearingPing = 0.5f; //Amount of seconds to update hearing range (after hearingPing seconds, it will try to find a new noise)
	private bool hearingPingCooldown = false;
	public float secondsAtNoiseLocation = 10f;
	private bool wasEngaging = false;
	public List <GameObject> soundEmitters; //List of sound emitters in range
	private bool startedEngaging = false;
	private bool canStopEngaging = false;

	[Header("Attack Settings")]
	public float distanceToAttack = 0.5f; //Minimal distance to start attacking
	public int attackPower = 60;
	public float attackCooldown = 1f;
	private bool hasAttacked = false;

	public event Action OnStateChanged;

	private void Awake()
	{
		enemyType = enemyTypeEnum.ToString();
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneUnloaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneUnloaded;
	}

	public void OnSceneUnloaded(Scene scene, LoadSceneMode mode)
	{
		AudioManager.Singleton.gameObject.GetComponent<NoiseEmitterController>().OnNoiseEmitted -= ListeningForNoise;
	}

	private void Start()
	{
		agent = GetComponentInChildren<NavMeshAgent>();
		pointIndex = 0;
		saveSecondsAtEachPoint = secondsAtEachPoint;

		Physics.IgnoreCollision(GetComponentInChildren<Collider>(), GameObject.FindObjectOfType<FirstPersonAIO>().gameObject.GetComponent<Collider>());

		FindPatrollingPoints();

		state = ASEState.Patrolling;

		if(patrollingPoints.Count > 0)
			SetDestination(patrollingPoints[pointIndex]);

		AudioManager.Singleton.gameObject.GetComponent<NoiseEmitterController>().OnNoiseEmitted += ListeningForNoise;
	}

	private void Update()
	{
		if (!this.gameObject.activeSelf) return;

		if(GameObject.FindObjectOfType<HealthSystemManager>().GetCombatState() == PlayerCombatStateEnum.Dead)
		{
			PatrollingLogic();
			return;
		}

		switch (state)
		{
			case ASEState.Patrolling:
				PatrollingLogic();
				break;
			case ASEState.Engaging:
				EngagingLogic();
				break;
			case ASEState.Attacking:
				AttackingLogic();
				break;
			default:
				break;
		}

		distanceFromPlayer = GetDistanceFromPlayer();
	}

	private void FindPatrollingPoints()
	{
		for (int i = patrollingPoints.Count - 1; i >= 0; i--)
		{
			// Check if the item is null
			if (patrollingPoints[i] == null || !patrollingPoints[i].activeSelf)
			{
				// Remove the item at the current index
				patrollingPoints.RemoveAt(i);
			}

			//Resets pointIndex if it's greater than patrollingPoints list size
			if (pointIndex > patrollingPoints.Count - 1)
				pointIndex = 0;

			if (currentTarget == null)
			{
				if (patrollingPoints.Count == 1) pointIndex = 0;
				currentTarget = patrollingPoints[pointIndex];
			}
		}

		foreach (Transform child in transform)
		{
			if (child.gameObject.CompareTag("PatrolPoint") && !patrollingPoints.Contains(child.gameObject) && child.gameObject.activeSelf)
				patrollingPoints.Add(child.gameObject);
		}
	}

	private void SetDestination(GameObject target)
	{
		if (agent.pathPending) return;

		currentTarget = target;
		if (currentTarget == null && patrollingPoints.Count > 0)
		{
			currentTarget = patrollingPoints[0];
		} 
		
		if(currentTarget != null)
			agent.SetDestination(currentTarget.transform.position);
	}

	#region Patrolling Logic

	private void PatrollingLogic()
	{
		if (patrollingPoints.Count == 0) return;

		distanceToTarget = ExtensionMethods.GetPathRemainingDistance(agent);

		agent.speed = speedPatrolling;

		if (distanceToTarget <= stoppingDistance)
		{
			if (!reachedDestination)
			{
				reachedDestination = true;
				FindPatrollingPoints();
				StartCoroutine(IdleAtPoint());
			}
		}
		else
		{
			SetDestination(patrollingPoints[pointIndex]);
		}
	}

	public override float CalculatePathDistance(GameObject target)
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

	public float GetDistanceFromPlayer()
	{
		return CalculatePathDistance(GameObject.FindObjectOfType<FirstPersonAIO>().gameObject);
	}

	private IEnumerator IdleAtPoint()
	{
		yield return new WaitForSeconds(secondsAtEachPoint);

		if (patrollingPoints.Contains(noiseTarget.gameObject))
		{
			patrollingPoints.Remove(noiseTarget.gameObject);
			secondsAtEachPoint = saveSecondsAtEachPoint;
		}

		pointIndex++;

		if (pointIndex >= patrollingPoints.Count)
			pointIndex = 0;

		SetDestination(patrollingPoints[pointIndex]);
		reachedDestination = false;
	}

	#endregion

	#region Engaging Logic

	private void EngagingLogic()
	{
		distanceToPlayer = CalculatePathDistance(GameObject.FindObjectOfType<FirstPersonAIO>().gameObject);
		if (GameObject.FindObjectOfType<HealthSystemManager>().playerHidingState == PlayerHidingEnum.Hiding 
			&& distanceToPlayer >= GameObject.FindObjectOfType<HealthSystemManager>().distanceFromHiding)
		{
			print("Player hid from ASE");
			state = ASEState.Patrolling;
			secondsAtEachPoint = (int) secondsAtNoiseLocation;
			return;
		}

		if(soundSource == null)
		{
			state = ASEState.Patrolling;
			return;
		}
		if (!hearingPingCooldown)
		{
			hearingPingCooldown = true;
			agent.speed = speedEngaging;
			wasEngaging = true;

			//if (!soundEmitters.Contains(soundSource) && soundSource.GetComponent<NoiseEmitter>())
			//{
			//	soundEmitters.Add(soundSource);
			//}

			StartCoroutine(FollowNoiseTarget());
		}

		distanceToTarget = CalculatePathDistance(soundSource);

		if (distanceToTarget <= distanceToAttack)
		{
			//AttackingLogic();
			state = ASEState.Attacking;
		}
	}

	//Subscribed to NoiseEmitterController event which is called by noise emitter objects (like player, doors...)
	private void ListeningForNoise(GameObject target, float noiseDistance, int noisePriority)
	{
		if (target == null || noiseTarget == null) return;

		//Calculates distance between this and the target
		distanceToTarget = CalculatePathDistance(target);

		if (distanceToTarget <= noiseDistance)
		{
			//Is within sound source engaging range
			soundSource = target;

			if (!soundEmitters.Contains(soundSource) && soundSource.GetComponent<NoiseEmitter>())
			{
				soundEmitters.Add(soundSource);
			}

			PrioritizeSoundSource(target);
			state = ASEState.Engaging;

			if (!startedEngaging)
			{
				startedEngaging = true;
				StartCoroutine(CanStopEngagingDelay());
			}
		}
		else
		{
			if (soundEmitters.Contains(target)) soundEmitters.Remove(target);
			//Will disengage (or remain in patrol if wasn't engaging)
			if (wasEngaging && canStopEngaging)
			{
				//if (soundEmitters.Contains(target)) soundEmitters.Remove(target);

				//Lost sight of noise source and will go back to patrolling
				startedEngaging = false;
				canStopEngaging = false;
				wasEngaging = false;

				if (!patrollingPoints.Contains(noiseTarget.gameObject))
					patrollingPoints.Add(noiseTarget.gameObject);

				pointIndex = patrollingPoints.Count - 1;
				SetDestination(patrollingPoints[pointIndex]);
				secondsAtEachPoint = (int) secondsAtNoiseLocation;
				state = ASEState.Patrolling;
			}
		}
	}

	private IEnumerator CanStopEngagingDelay()
	{
		yield return new WaitForSeconds(3f);
		canStopEngaging = true;
	}

	private void PrioritizeSoundSource(GameObject target)
	{
		if (soundEmitters.Count > 1)
		{
			//currentTarget is NoiseEmitter
			if (target.GetComponent<NoiseEmitter>())
			{
				GameObject highestPriority = soundEmitters[0];
				for (int i = 1; i < soundEmitters.Count; i++)
				{
					if (soundEmitters[i].GetComponent<NoiseEmitter>().noisePriority > highestPriority.GetComponent<NoiseEmitter>().noisePriority)
					{
						highestPriority = soundEmitters[i];
					}
				}
				noiseTarget.position = highestPriority.transform.position;
				SetDestination(noiseTarget.gameObject);
			}
		}
		else
		{
			noiseTarget.position = target.transform.position;
			SetDestination(noiseTarget.gameObject);
			//if (GetCurrentTarget() != target)
			//	ChangeTarget(target);
		}
	}

	private void ChangeTarget(GameObject target)
	{
		currentTarget = target;
	}

	private IEnumerator FollowNoiseTarget()
	{
		SetDestination(noiseTarget.gameObject);
		yield return new WaitForSeconds(hearingPing);
		hearingPingCooldown = false;
		yield return null;
	}

	private GameObject GetCurrentTarget()
	{
		return currentTarget;
	}

	#endregion

	#region Attacking Logic

	private void AttackingLogic()
	{
		if (!hasAttacked)
		{
			//Will attack player
			SetDestination(noiseTarget.gameObject);
			hasAttacked = true;
			state = ASEState.Attacking;

			GameObject locker = GameObject.FindObjectOfType<HealthSystemManager>().GetLocker();
			if (locker != null)
			{
				locker.GetComponentInChildren<Animator>().Play("DoorOpenAnim");
			}

			StartCoroutine(AttackCoroutine());
		}
	}

	private IEnumerator AttackCoroutine()
	{
		agent.speed = 0;
		agent.velocity = Vector3.zero;

		distanceToTarget = CalculatePathDistance(currentTarget);

		if (distanceToTarget <= distanceToAttack)
		{
			GameObject.FindObjectOfType<HealthSystemManager>().ReceiveDamage(attackPower);
			print("Attacking");
		}	

		yield return new WaitForSeconds(attackCooldown);

		state = ASEState.Engaging;
		agent.speed = speedEngaging;
		hasAttacked = false;
	}

	#endregion

	private void OnDestroy()
	{
		AudioManager audio = GameObject.FindObjectOfType<AudioManager>();
		if(audio != null)
			AudioManager.Singleton.gameObject.GetComponent<NoiseEmitterController>().OnNoiseEmitted -= ListeningForNoise;
	}

	public void LoadData(GameData gameData)
	{
		if (gameData == null) return;

		// Check if the enemy's data exists in the save file
		if (enemyType == null)
			enemyType = enemyTypeEnum.ToString();
		if (gameData.ASEData.ContainsKey(enemyType))
		{
			// Retrieve the saved data
			SaveASEData savedData = gameData.ASEData[enemyType];

			// Apply the saved data to this enemy's current state
			speedPatrolling = savedData.speedPatrolling;
			speedEngaging = savedData.speedEngaging;
			noiseRange = savedData.noiseRange;
			hearingPing = savedData.hearingPing;
			distanceToAttack = savedData.distanceToAttack;
			attackCooldown = savedData.attackCooldown;
		}
		else
		{
			Debug.LogWarning("No saved data found for enemy with ID: " + enemyTypeEnum);
		}
	}

	public void SaveData(GameData gameData)
	{
		if (gameData == null) return;

		SaveASEData saveASEdata = new SaveASEData(speedPatrolling, speedEngaging, noiseRange, hearingPing, distanceToAttack, attackCooldown);

		//Checks if id is already contained in the itemsLooted dictionary
		if(enemyType == null)
			enemyType = enemyTypeEnum.ToString();

		if (gameData.ASEData.ContainsKey(enemyType))
		{
			//Id already in dictionary
			gameData.ASEData.Remove(enemyType);
		}

		//Adds item in its current state to save file
		gameData.ASEData.Add(enemyType, saveASEdata);
	}

	public string GetId()
	{
		return "";
	}
}

public enum ASEState { Patrolling, Engaging, Attacking}

[System.Serializable]
public class SaveASEData
{
	//Patrolling Settings to save
	public float speedPatrolling;

	//Engaging Settings to save
	public float speedEngaging;
	public float noiseRange;
	public float hearingPing; //Amount of seconds to update hearing range (after hearingPing seconds, it will try to find a new noise)

	//Attack Settings to save
	public float distanceToAttack; //Minimal distance to start attacking
	public float attackCooldown;

	public SaveASEData(float speedPatrolling, float speedEngaging, float noiseRange, float hearingPing, float distanceToAttack, float attackCooldown)
	{
		this.speedPatrolling = speedPatrolling;
		this.speedEngaging = speedEngaging;
		this.noiseRange = noiseRange;
		this.hearingPing = hearingPing;
		this.distanceToAttack = distanceToAttack;
		this.attackCooldown = attackCooldown;
	}
}

