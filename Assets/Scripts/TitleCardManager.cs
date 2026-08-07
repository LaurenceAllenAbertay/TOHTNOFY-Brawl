using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleCardManager : MonoBehaviour
{
	private void Start()
	{
		StartCoroutine(MoveScene());
	}

	private void Update()
	{
		if (Input.anyKeyDown)
		{
			SceneManager.LoadScene("DemoLobby");
		}
	}

	private IEnumerator MoveScene()
	{
		yield return new WaitForSeconds(7.99f);
		SceneManager.LoadScene("DemoLobby");
	}
}
