﻿using UnityEngine;
using System.Collections;

public class CollectableBehaviour : MonoBehaviour {

	private Collectable _collectable;

	private float _lifetimeInSecs = 0.0f;
	public float Lifetime {
		get { return _lifetimeInSecs; }
	}

	// Use this for initialization
	void Start () {
		//TODO change sprite based on the type?
		_collectable = Collectable.newRandomCollectable();
	}

	void Update() {
		//TODO clean up after some time?
		_lifetimeInSecs += Time.deltaTime;
	}

	void OnTriggerEnter(Collider other) {
		if (other.tag == "spaceship") {
			//This code could also be in PlayerController.. refactor if needed
			PlayerController ctrl = other.GetComponent<PlayerController>();
			ctrl.addCollectable(_collectable);
			Destroy(this.gameObject);
		}
		else if (other.tag == "planet") {
			//destroy just so that it doesn't stay within the planet looking stupid.
			//spawning these should however check that this doesn't happen, but leave this just in case
			Destroy(this.gameObject);
		}
	}
}