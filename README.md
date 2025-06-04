These are some examples of scripts I've been creating for my horror games.

1. EnemyBase
   - As the name suggests, it's the base script for all enemies in the game and controls their basic AI functions.
   - It includes a NavMeshAgent for AI navigation and a method CalculatePathDistance() to determine the total distance to a target using Unity's NavMesh system and an enum to define the enemy type.

2. ASE
   - This C# script defines the ASE enemy class, which extends EnemyBase and implements IDataPersistence.
   - Manages the enemy’s state (ASEState), movement, target tracking, and noise detection.
   - It provides AI behaviors such as patrolling, engaging, and attacking using Unity's NavMeshAgent, where:
       - Patrolling: Moves between defined points using pathfinding, pausing at each destination for a set duration.
       - Engaging: Reacts to noise sources by following sound emitters, prioritizing high-priority noises, and transitioning between states.
       - Attacking: Triggers damage when the enemy reaches the player within attack range.
    - Data Persistence Saves and loads enemy-related data, ensuring behavior consistency across game sessions.


