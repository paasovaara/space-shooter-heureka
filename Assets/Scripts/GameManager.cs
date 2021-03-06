﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using DG.Tweening;
using UniRx;

public class GameManager: Singleton<GameManager>, Timeoutable.TimeoutListener {
	[SerializeField]
	private GameObject _explosionPrefab;
	[SerializeField]
	private Transform _gameArea;

	[Header("Game play")]
	[SerializeField]
	private GameObject _timeDisplay;
	[SerializeField]
	private Text _timeText;
	[SerializeField]
	private float gravityForce = 100.0f;
	[SerializeField]
	private float _gravityRange = 120.0f;

	[Header("Orbit items")]
	[SerializeField]
	[Range(0.0f, 1.0f)]
	private float _orbitItemSpawnChance = 0.5f;
	[SerializeField]
	private float _orbitItemSpawnAttempInterval = 20.0f;
	[SerializeField]
	private GameObject[] _orbitItems;
	[SerializeField]
	private Transform _orbitItemContainer;

	private float _lastOrbitItemSpawnAttempTime;

	[Header("Planets")]
	[SerializeField]
	private Transform _homePlanet;
	[SerializeField]
	private List<Rigidbody> _planets;

	[Header("Players")]
	[SerializeField]
	private List<GameObject> _spaceShipPrefabs;
	[SerializeField]
	private Transform _playerContainer;

	[Header ("Collectables")]
	[SerializeField]
	private GameObject _collectablePrefab;
	[SerializeField]
	private Transform _collectableContainer;

	[Header ("Asteroids")]
	[SerializeField]
	private GameObject _asteroidPrefab;
	[SerializeField]
	private Transform _asteroidContainer;

	private List<GameObject> _asteroids = new List<GameObject> ();
	private float _secsUntilNextCollectable = 0.0f;
	private bool _warningGiven = false;
	private Dictionary<int, PlayerState> _playerShips = new Dictionary<int, PlayerState>();
	private bool _isGameActive = false;

	// Game time related
	private float _gameStartedTime;
	private float _gameEndTime;

  	private class PlayerState
	{
		public GameObject 	_ship;
		public Rigidbody 	_rigidbody;
		public float 		_idleTime = 0.0f;

		public PlayerState (GameObject obj)
		{
			_ship = obj;
			_rigidbody = obj.GetComponent<Rigidbody> ();
		}
	}

	private void Start ()
	{
		string[] joysticks = Input.GetJoystickNames();
		foreach (string j in joysticks)
		{
			Debug.Log("Found joystick " + j);
		}

		for (int n = 0; n < GameConstants.NUMBER_OF_ASTEROIDS; n++)
		{
			CreateAsteroid ();
		}
	}

	public void StartNewRound (float roundLength)
	{
		//TODO enumerate min and max
		for (int id = 0; id < GameConstants.NUMBER_OF_PLAYERS; id++)
		{
			CreatePlayer(id);
		}

		InitCollectableTimeout();
		_warningGiven = false;
		_isGameActive = true;
		AudioManager.Instance.Speak("Board your ships.");

		// Show the time display
		_gameStartedTime = Time.time;
		_gameEndTime = Time.time + roundLength;
		_timeDisplay.SetActive (true);
		_timeText.text = string.Empty;
	}

	public void StopRound ()
	{
		//TODO modify state so players cannot re-create themselves
		DestroyAll ();
		_isGameActive = false;
		AudioManager.Instance.Speak("Time's up.");

		// Hide the time display
		_gameStartedTime = -1.0f;
		_gameEndTime = -1.0f;
		_timeDisplay.SetActive (false);
		_timeText.text = string.Empty;
	}

	private void InitCollectableTimeout ()
	{
		//Rate is loosely controlled by the number of active ships
		float timeout = 10.0f;

		if (_playerShips.Count < 4)
		{
			timeout = 10.0f;
		}
		else {
			timeout = 5.0f;
		}
		_secsUntilNextCollectable = Mathf.Clamp(timeout * Random.value, 1.0f, timeout);
	}

	private void Awake ()
	{
		// Register a function to take care of possibly invalidly destroyed asteroids
		Observable.Interval (System.TimeSpan.FromSeconds (4.0f)).Subscribe (_ => {
			UpdateAsteroids ();
		});
	}
		
	private void Update ()
	{
		if (!_isGameActive)
		{
			return;
		}

		//TODO remove direct button listening from this class... abstract it somewhere and just get events, etc
		for (int id = 0; id < GameConstants.NUMBER_OF_PLAYERS; id++)
		{
			if (!_playerShips.ContainsKey(id))
			{
				GameConstants.PlayerKeys keys = GameConstants.getPlayerKeys(id);
				bool pressed = Input.GetButtonDown(keys.SpawnBtn);

				if (pressed)
				{
					Debug.LogFormat ("Spawnbutton {0} pressed for ship ID {1}", keys.SpawnBtn, id);
					CreatePlayer(id);
				}

			}
		}

		_secsUntilNextCollectable -= Time.deltaTime;

		if (_secsUntilNextCollectable <= 0.0f)
		{
			//These should also be cleaned up.. Add logic to CollectableBehaviour or here
			createCollectable();
			InitCollectableTimeout();
		}

		if (!_warningGiven)
		{
			float secsLeft = 30.0f;
			if (SessionManager.Instance.gameSessionLeft() < secsLeft)
			{
				AudioManager.Instance.Speak ("You have thirty seconds");
				_warningGiven = true;
			}
		}

		// Update the time view text
		_timeText.text = string.Format ("{0} SEC", (int)(_gameEndTime-Time.time));

		// Attempt to spawn orbit items at regular intervals
		UpdateOrbitItems ();
	}

	private void UpdateAsteroids ()
	{
		Debug.Log ("GameManager UpdateAsteroids: Updating asteroids");

		if (!_isGameActive)
		{
			return;
		}

		int numAsteroids = _asteroids.Count;
		int numInvalidAsteroids = _asteroids.RemoveAll (go => go == null);

		if (numInvalidAsteroids > 0)
		{
			Debug.LogFormat ("GameManager UpdateAsteroids: Replacing {0} invalid asteroids", numInvalidAsteroids);

			for (int i = 0; i < numInvalidAsteroids; ++i)
			{
				CreateAsteroid ();
			}
		}
	}

	private void UpdateOrbitItems ()
	{
		if (Time.time - _lastOrbitItemSpawnAttempTime > _orbitItemSpawnAttempInterval)
		{
			if (Random.value > 1.0f - _orbitItemSpawnChance)
			{
				int numOrbitItems = _orbitItems.Length;

				if (numOrbitItems > 0)
				{
					int index = Random.Range (0, numOrbitItems);
					Debug.LogFormat ("GameManager Update: Spawning orbit item: {0}", index);

					GameObject orbitItem = Instantiate (_orbitItems[index], _orbitItemContainer) as GameObject;
					Orbit orbit = OrbitManager.Instance.GetOrbit ();
					orbitItem.transform.position = orbit.Path[0];

					orbitItem.transform.DOPath (orbit.Path, orbit.Duration, PathType.CatmullRom, PathMode.TopDown2D)
						.SetEase (Ease.Linear)
						.OnComplete (() => {
							if (orbitItem != null)
							{
								Destroy (orbitItem);
							}
						});
				}
			}

			_lastOrbitItemSpawnAttempTime = Time.time;
		}
	}

	private void FixedUpdate ()
	{
		// Apply gravity towards planets to players
		int numPlanets = _planets.Count;
		int numPlayers = _playerShips.Count;

		foreach (PlayerState player in _playerShips.Values)
		{
			bool outsideRangeOfAllPlanets = true;

			for (int i = 0; i < numPlanets; ++i)
			{
				Rigidbody planetRigidbody = _planets[i];

				// No movement towards the y direction (no depth)
				Vector3 direction = (planetRigidbody.position - player._rigidbody.position);
				direction.y = 0.0f;

				float distSqr = direction.sqrMagnitude;
				float magnitude = (gravityForce * planetRigidbody.mass * player._rigidbody.mass) / distSqr;
				direction = direction.normalized;

				// If the distance squared is less than gravity range
				// add the force to the ship
				if (distSqr < _gravityRange)
				{
					player._rigidbody.AddForce (magnitude * direction * Time.fixedDeltaTime);
					outsideRangeOfAllPlanets = false;
				}
			}

			// If outside of gravity range for all planets zero all the forces
			if (outsideRangeOfAllPlanets)
			{
				player._rigidbody.velocity = Vector3.zero;
				player._rigidbody.angularVelocity = Vector3.zero;
			}
		}
	}

	/**
	 * Note! Create and Destroy spaceship instances ONLY through this class!
	 * This singleton instance keeps the track who is spawned, when and where
	 * */

	public void DestroyWithExplosion (GameObject obj, int playerId =-1)
	{
		DestroyWithExplosion (obj, true, true, playerId);
	}

	public void DestroyWithExplosion (GameObject obj, bool scores, bool sounds, int playerId =-1)
	{
		//TODO add tags to constants
		if (obj.CompareTag ("Player"))
		{
			PlayerController playerController = obj.GetComponent<PlayerController> ();
			int id = playerController.Id;

			if (_playerShips.Remove (id))
			{
				Debug.LogFormat ("GameManager DestroyWithExplosion: Player {0} died.", id);

				if (scores)
				{
					ScoreManager.Instance.addPoints (id, GameConstants.POINTS_FOR_DYING);
				}
				if (sounds)
				{
					InsultManager.Instance.playerDied (id);
					AudioManager.Instance.playClip (AudioManager.AppAudioClip.Explosion);
				}
			}
		}

		AnimateExplosion (obj.transform.position, playerId);
		Destroy (obj);
	}

	private void DestroyAll ()
	{
		foreach (PlayerState s in _playerShips.Values)
		{
			GameObject obj = s._ship;
			AnimateExplosion (obj.transform.position);
			Destroy (obj);
		}

		_playerShips.Clear ();
		AudioManager.Instance.playClip (AudioManager.AppAudioClip.Explosion);
	}

    public void AnimateExplosion (Vector3 position, int playerId =-1, float scaleFactor = -1.0f)
	{
		GameObject explosion = null;

		if (playerId == -1)
		{
			explosion = Instantiate (_explosionPrefab) as GameObject;
		}
		else
		{
			explosion = Instantiate (PlayerInformationManager.Instance.GetPlayerInformation (playerId).ExplosionPrefab) as GameObject;
		}

		explosion.transform.position = position;
        if (scaleFactor > 0.0f) {
            explosion.transform.localScale *= scaleFactor;
        }

		float duration = explosion.GetComponent<ParticleSystem> ().duration;
		Destroy (explosion, duration);
	}


	public void DestroyAsteroid (GameObject obj, int playerId = -1)
	{
		Debug.Log ("destroy asteroid");

		if (_asteroids.Remove(obj))
		{
			//create new to replace old one
			Debug.Log ("Creating a new asteroid to replace old");
			CreateAsteroid ();
		}
		else
		{
			Debug.LogWarning ("Destroying asteroid that's not on the list!");
		}

		AsteroidBehaviour behaviour = obj.GetComponent<AsteroidBehaviour> ();
		behaviour.DestroyMe (playerId);
	}

	private void CreatePlayer (int id)
	{
		if (id < 0 || id >= 8)
		{
			Debug.LogErrorFormat ("GameManager CreatePlayer: Invalid id: {0} maximum id is 7", id);
		}

		int index =  id % _spaceShipPrefabs.Count;
		GameObject prefab = _spaceShipPrefabs[index];
		GameObject ship = Instantiate (prefab, _playerContainer) as GameObject;
		_playerShips[id] = new PlayerState(ship);

		//init game controller keys. See "Project Settings > Input" where the id mapping is
		GameConstants.PlayerKeys keys = GameConstants.getPlayerKeys(id);
		PlayerController ctrl = ship.GetComponent<PlayerController>();
		ctrl.Init (id, keys, PlayerInformationManager.Instance.GetPlayerInformation (id));
		ScoreManager.Instance.addPoints (id, 0);

		ship.GetComponent<WeaponLauncher>().setFireKeyCode (keys.FireBtn);

		//set initial position to unique place around the home planet
		Vector3 directionOnUnitCircle = GameConstants.getPlayerStartDirection(id);
		Vector3 startPos = _homePlanet.position + 1.2f * _homePlanet.localScale.magnitude * directionOnUnitCircle; 
		ship.transform.position = startPos;

		ctrl.addTimeoutListener(this);
	}

	public void timeoutElapsed(Timeoutable t)
	{
		//TODO refactor tags out, use method accesesor for this feature
		if (t.CompareTag ("spaceship"))
		{
			//Invoked by the Controllers after a timeout
			SpaceShipController spaceShip = t.GetComponent<SpaceShipController> ();
			DestroyWithExplosion(spaceShip.Player.gameObject, false, false);
		}
		else
		{
			Destroy(t.gameObject);
		}
	}


	private void createCollectable() {
		GameObject collectable = Instantiate(_collectablePrefab, _collectableContainer) as GameObject;
		float randomX = (Random.value - 0.5f) * _gameArea.localScale.x;
		float randomZ = (Random.value - 0.5f) * _gameArea.localScale.z;

		collectable.transform.position = new Vector3(randomX, 0.0f, randomZ); 

		collectable.GetComponent<CollectableBehaviour>().addTimeoutListener(this);
	}

	private void CreateAsteroid ()
	{
		//TODO some animation
		GameObject asteroid = Instantiate (_asteroidPrefab, _asteroidContainer) as GameObject;

		if (Random.value > 0.05f)
		{
			asteroid.AddComponent<AsteroidBehaviour>();
		}
		else
		{
			asteroid.AddComponent<FallingAsteroidBehaviour>();
		}

		//TODO make sure it doesn't overlap with the planet
		float randomX = (Random.value - 0.5f) * _gameArea.localScale.x;
		float randomZ = (Random.value - 0.5f) * _gameArea.localScale.z;
		asteroid.transform.position = new Vector3(randomX, 0.0f, randomZ); 

		float scaleFactor = Random.Range(0.1f, 0.3f);
		asteroid.transform.localScale = scaleFactor * asteroid.transform.localScale;
		_asteroids.Add(asteroid);
	}
	

}
