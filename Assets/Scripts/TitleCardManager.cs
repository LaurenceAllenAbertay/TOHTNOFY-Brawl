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
			SceneManager.LoadScene("Main Menu");
		}
	}

	private IEnumerator MoveScene()
	{
		yield return new WaitForSeconds(8f);
		SceneManager.LoadScene("Main Menu");
	}
}
