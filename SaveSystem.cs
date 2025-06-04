using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using System.Linq;
using UnityEngine.SceneManagement;

//Is held in the SaveManager object
public class SaveSystem : MonoBehaviour
{
    [Header("Debugging")]
    [SerializeField] private bool disableDataPersistence = false;

	[Header("Save File Settings")]
	public string fileName;
    [SerializeField] private bool useEncryption = true;

	[Header("Dialogue File Settings")]
	[SerializeField] private TextAsset loadGlobalsJSON;

	private GameData gameData, gameDataTemporary;
	private SaveFileHandler saveFileHandler;

	private List<IDataPersistence> dataPersistenceObjects = null;

	private DialogueVariables dialogueVariables;
    private DialogueManager dialogueManager;

    private bool newGame = false;

    private string selectedProfileId = "";

    private bool loadData = false;

    [HideInInspector] public bool loadedGameFromMenu = false;

	#region Singleton

	public static SaveSystem Singleton;

    #endregion

    private void Awake()
    {
        if (Singleton == null)
        {
            Singleton = this;
        }
        else
        {
            Destroy(this.gameObject);
        }

		this.saveFileHandler = new SaveFileHandler(Application.persistentDataPath, fileName, useEncryption);

		this.selectedProfileId = saveFileHandler.GetMostRecentlyUpdatedProfileId();
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	public void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		this.dataPersistenceObjects = FindAllDataPersistenceObjects();
	}

	private void Start()
	{
		dialogueManager = GameObject.FindObjectOfType<DialogueManager>();
        dialogueManager.OnDialogueVariablesInitialized += DialogueVariablesInitialized;
	}

	public GameData GetGameData()
    {
        return gameData;
    }

    public bool GetEncryptState()
    {
        return useEncryption;
    }
    public SaveFileHandler GetSaveFileHandler()
    {
        return saveFileHandler;
    }

	//Subscribed to DialogueManager event
	private void DialogueVariablesInitialized()
    {
        dialogueVariables = dialogueManager.dialogueVariables;
    }

    public DialogueVariables _GetDialogueVariables(bool permanent)
    {
        return dialogueVariables;
    }

    public string GetSelectedProfileId()
    {
        return selectedProfileId;
	}

	//Finds all objects that have IDataPersistence interface implemented
	private List<IDataPersistence> FindAllDataPersistenceObjects()
	{
        //The true garantees that disabled objects are also saved on the list
		IEnumerable<IDataPersistence> dataPersistenceObjects = FindObjectsOfType<MonoBehaviour>(true).OfType<IDataPersistence>();
        return new List<IDataPersistence>(dataPersistenceObjects);
	}

    public void NewGame()
    {
        ChangeSelectedProfileId("Slot4");
		
        dialogueVariables = new DialogueVariables(loadGlobalsJSON);
        dialogueManager.dialogueVariables = dialogueVariables;

		loadData = false;
		SaveSystem.Singleton.loadedGameFromMenu = false;

		newGame = true;
		this.gameData = new GameData();
	}

    //Loads file (if present) and calls all data persistence objects to load their respective data
    public void LoadData()
    {
        if (disableDataPersistence || !loadData)
        {
            loadData = true;
            return;
        }

		newGame = false;

        if (loadedGameFromMenu)
        {
			//Loaded game from menu = load all iDataPersistance objects with gameData information from file
            loadedGameFromMenu = false;
			if (gameData != null)
			{
				//Loads save data from file using SaveFileHandler
				if (!newGame)
				{
					this.gameData = saveFileHandler.Load(selectedProfileId);
				}
			}
			else
			{
				this.gameData = saveFileHandler.Load(selectedProfileId);
			}

			//Pushes the loaded data to all scripts that have data persistence
			foreach (IDataPersistence dataPersistenceObject in dataPersistenceObjects)
			{
				dataPersistenceObject.LoadData(gameData);
			}
		}
		else
        {
            //Just started new game or loaded different scene inside the game = load all iDataPersistance objects with gameDataTemporary information
			foreach (IDataPersistence dataPersistenceObject in dataPersistenceObjects)
			{
				dataPersistenceObject.LoadData(gameData);
			}
		}
	}

	public void LoadTemporaryData()
	{
		if (disableDataPersistence) return;
		print("Attempting to load gameDataTemporary data");

		gameDataTemporary = saveFileHandler.Load(selectedProfileId);

		//gameDataTemp is null, meaning new game started
		if (gameDataTemporary == null)
			gameDataTemporary = new GameData();

		newGame = false;

		//Pushes the loaded data to all scripts that have data persistence
		foreach (IDataPersistence dataPersistenceObject in dataPersistenceObjects)
		{
			dataPersistenceObject.LoadData(gameDataTemporary);
		}
	}

    //Called whenever scene is loaded
	public void LoadDialogue()
	{
		dialogueVariables = saveFileHandler.LoadDialogue(new DialogueVariables(loadGlobalsJSON), selectedProfileId);
	}

    public DialogueVariables LoadDialogueFromFile()
    {
		DialogueVariables diaVar = saveFileHandler.LoadDialogue(new DialogueVariables(loadGlobalsJSON), selectedProfileId);
        return diaVar;
	}

	public DialogueVariables GetDialogueVariables()
	{
		return dialogueVariables;
	}

	//Saves file and calls all data persistence objects to save their respective data
	public void SaveData()
    {
		if (disableDataPersistence) return;

		if (this.gameData == null)
            this.gameData = new GameData();

		//Passes data from all scripts that have data persistence
		foreach (IDataPersistence dataPersistence in dataPersistenceObjects)
        {
            dataPersistence.SaveData(gameData);
        }

		gameData.AddSaveCount();
		gameData.GetSavedSceneName();
		gameData.UpdateDateTime();
		gameData.SetTime();

		//Saves data to file using SaveFileHandler
		saveFileHandler.SaveData(gameData, selectedProfileId);
    }

	//Called by door object
	public void SaveTemporaryData()
	{
		if (disableDataPersistence) return;

		if (this.gameDataTemporary == null)
		{
			this.gameDataTemporary = this.gameData;
			if (this.gameDataTemporary == null)
			{
				this.gameDataTemporary = new GameData();
			}
		}

		FindAllDataPersistenceObjects();

		//Recives data from all scripts that have data persistence
		foreach (IDataPersistence dataPersistence in dataPersistenceObjects)
		{
			dataPersistence.SaveData(gameDataTemporary);
		}
	}

	public void SaveDialogue()
	{
        saveFileHandler.SaveDialogue(dialogueManager.dialogueVariables, selectedProfileId);
	}

	//Creates a dictionary with all gameData files and their respective Ids
	public Dictionary<string, GameData> GetAllProfilesGameData()
    {
        return saveFileHandler.LoadAllProfiles();
    }

    public void ChangeSelectedProfileId(string newProfileId)
    {
        //Updates the profile to use for saving and loading
        selectedProfileId = newProfileId;
    }

	public void ChangeSelectedProfileIdAndLoad(string newProfileId)
	{
		//Updates the profile to use for saving and loading
		selectedProfileId = newProfileId;
        //Loads game with selected profile
        LoadData();
	}

	public void DeleteTemporaryFile()
	{
		saveFileHandler.DeleteTemporarySave(selectedProfileId);
	}

	private void OnApplicationQuit()
	{
		DeleteTemporaryFile();
	}

	private void OnDestroy()
	{
		if(dialogueManager != null)
            dialogueManager.OnDialogueVariablesInitialized -= DialogueVariablesInitialized;
	}

    public void SetLoadDataState(bool state)
    {
        loadData = state;
	}






	#region Save/Load Generic

	public void Save()
    {
        JObject jSaveGame = SerializeData();

        //Saves serialized data into file
        string filePath = Application.persistentDataPath + "/TestSave.save";
        StreamWriter sw = new StreamWriter(filePath);
        print("Saving to " + filePath);
        sw.WriteLine(jSaveGame.ToString());
        sw.Close();
    }

    private JObject SerializeData()
    {
        JObject jSaveGame = new JObject();

        //Saves every item inside ISerializable List
        //for (int i = 0; i < objectsToSaveList.Count; i++)
        //{
            //jSaveGame.Add(objectsToSaveList[i].GetComponent<IDataPersistence>().GetId(), objectsToSaveList[i].GetComponent<IDataPersistence>().Serialize());
        //}

        //jSaveGame.Add(playerSave.GetId(), playerSave.Serialize());

        return jSaveGame;
    }

    public void Load()
    {
        //loads information from file into a string
        string filePath = Application.persistentDataPath + "/TestSave.save";
        print("Loading from" + filePath);
        StreamReader sr = new StreamReader(filePath);
        string jsonString = sr.ReadToEnd();
        sr.Close();

        JObject jLoadGame = JObject.Parse(jsonString); //Creates JObject from string

        Deserialize(jLoadGame);
    }

    private void Deserialize(JObject jLoadGame)
    {
        //Deserializes every item inside ISerializable List
        //for (int i = 0; i < objectsToSaveList.Count; i++)
        //{
            //string jString = jLoadGame[objectsToSaveList[i].GetComponent<IDataPersistence>().GetId()].ToString();
            //objectsToSaveList[i].GetComponent<IDataPersistence>().Deserialize(jString);
        //}

        //playerSave.Deserialize(playerJson);

    }

	#endregion

    #region Save/Load Instances

    public void SaveInstances()
    {
        JObject jSaveGame = SerializeInstancesData();

        //Saves serialized data into file
        string filePath = Application.persistentDataPath + "/TestSaveInstances.save";
        StreamWriter sw = new StreamWriter(filePath);
        print("Saving to " + filePath);
        sw.WriteLine(jSaveGame.ToString());
        sw.Close();
    }

    private JObject SerializeInstancesData()
    {
        JObject jSaveGame = new JObject();

        //Saves every item inside ISerializableInstance List savedObjectsToInstantiate
        for (int i = 0; i < InstantiationManager.Singleton.savedObjectsToInstantiate.Count; i++)
        {
            ISerializableInstance obj = InstantiationManager.Singleton.savedObjectsToInstantiate[i].GetComponent<ISerializableInstance>();
            jSaveGame.Add(obj.GetId(), obj.SerializeInstance());
        }

        //jSaveGame.Add(playerSave.GetId(), playerSave.Serialize());

        return jSaveGame;
    }

    public void LoadInstances()
    {
        JObject jLoadGame = LoadInstancesFile();

        DeserializeInstances(jLoadGame);
    }

    public JObject LoadInstancesFile()
    {
        //loads information from file into a string
        string filePath = Application.persistentDataPath + "/TestSaveInstances.save";
        print("Loading from" + filePath);
        StreamReader sr = new StreamReader(filePath);
        string jsonString = sr.ReadToEnd();
        sr.Close();

        JObject jLoadGame = JObject.Parse(jsonString); //Creates JObject from string

        return jLoadGame;
    }

    private void DeserializeInstances(JObject jLoadGame)
    {
        //Deserializes every item inside savedObjectsToInstantiate List
        for (int i = 0; i < InstantiationManager.Singleton.savedObjectsToInstantiate.Count; i++)
        {
            string jString = jLoadGame[InstantiationManager.Singleton.savedObjectsToInstantiate[i].GetComponent<ISerializableInstance>().GetId()].ToString();
            InstantiationManager.Singleton.savedObjectsToInstantiate[i].GetComponent<ISerializableInstance>().DeserializeInstance(jString);
        }
    }

    JObject _jLoadGame;

    public bool FindInstantiatedObject(string _id, bool firstTime)
    {
        if (firstTime)
            _jLoadGame = LoadInstancesFile();

        JObject jLoadGame = _jLoadGame;


        return jLoadGame.ContainsKey(_id);
    }

    #endregion
}
