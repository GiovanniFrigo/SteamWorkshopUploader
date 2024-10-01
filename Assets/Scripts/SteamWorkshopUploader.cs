using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using TinyJSON;


public class SteamWorkshopUploader : MonoBehaviour
{
    private const string SteamLegalAgreementUrl = "https://steamcommunity.com/sharedfiles/workshoplegalagreement";

    private const string RelativeBasePath = "/../WorkshopContent/";
    private const int Version = 5;

    public Text versionText;
    public Text statusText;
    public Button statusTextButton;
    public Slider progressBar;

    public RectTransform packListRoot;
    public RectTransform packListFrame;
    public GameObject packListButtonPrefab;
    public Text fatalErrorText;

    [Header("ModPack Interface")] public RectTransform currentItemPanel;
    public Text submitButtonText;
    public Text modPackContents;
    public RawImage modPackPreview;
    public GameObject previewNotFoundPlaceholder;
    public InputField modPackName;
    public InputField modPackTitle;
    public InputField modPackContentFolder;
    public InputField modPackChangeNotes;
    public InputField modPackDescription;
    public Dropdown modPackVisibility;
    public Dropdown motorsportTag;
    public Dropdown eraTag;

    private string basePath;

    private WorkshopModPack currentPack;
    private string currentPackFilename;
    private UGCUpdateHandle_t currentHandle = UGCUpdateHandle_t.Invalid;

    private CallResult<CreateItemResult_t> itemCreatedCallResult;
    private CallResult<SubmitItemUpdateResult_t> itemSubmittedCallResult;

    private string lastLoggedString;
    private float lastLogTime;

    private string statusUrl;

    private void Awake()
    {
        SetupDirectories();
        Screen.SetResolution(800, 760, fullscreen: false);
    }

    private void Start()
    {
        versionText.text = $"Golden Lap - Steam Workshop Uploader - Build {Version}";

        if (SteamManager.m_steamAppId == 0)
        {
            FatalError("Steam App ID isn't set! Make sure 'steam_appid.txt' is placed next to the '.exe' file and contains the correct appID.");
        }
        else if (!SteamManager.Initialized)
        {
            FatalError("Steam API not initialized.\n Make sure you have the Steam client running.");
        }
        else
        {
            RefreshPackList();
            RefreshCurrentModPack();
        }

        statusTextButton.onClick.AddListener(OnStatusTextClick);
        statusTextButton.interactable = false;
    }

    private void OnApplicationQuit()
    {
        if (currentPack != null)
        {
            OnCurrentModPackChanges();
            SaveCurrentModPack();
        }

        SteamAPI.Shutdown();
    }

    public void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            return;
        }

        RefreshPackList();

        if (currentPack != null)
        {
            RefreshCurrentModPack();
        }
    }

    public void Shutdown()
    {
        SteamAPI.Shutdown();
    }

    private void OnEnable()
    {
        if (SteamManager.Initialized)
        {
            itemCreatedCallResult = CallResult<CreateItemResult_t>.Create(OnItemCreated);
            itemSubmittedCallResult = CallResult<SubmitItemUpdateResult_t>.Create(OnItemSubmitted);
        }
    }

    private void FatalError(string message)
    {
        fatalErrorText.text = $"FATAL ERROR:\n{message}";
        fatalErrorText.gameObject.SetActive(true);

        packListFrame.gameObject.SetActive(false);
        currentItemPanel.gameObject.SetActive(false);
    }

    private void SetupDirectories()
    {
        basePath = Path.Join(Application.dataPath, RelativeBasePath);
        Debug.Log($"basePath is: {basePath}");

        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }
    }

    private string[] GetPackFilenames()
    {
        return Directory.GetFiles(basePath, "*.workshop.json", SearchOption.TopDirectoryOnly);
    }

    private void ClearPackList()
    {
        foreach (Transform child in packListRoot)
        {
            Destroy(child.gameObject);
        }
    }

    private void RefreshPackList()
    {
        ClearPackList();

        var paths = GetPackFilenames();

        // create list of buttons using prefabs
        // hook up their click events to the right function
        for (int i = 0; i < paths.Length; i++)
        {
            string packPath = paths[i];
            string packName = Path.GetFileName(packPath);

            var buttonObj = Instantiate(packListButtonPrefab, Vector3.zero, Quaternion.identity);
            var button = buttonObj.GetComponent<Button>();
            button.transform.SetParent(packListRoot);

            button.GetComponentInChildren<Text>().text = packName.Replace(".workshop.json", "");

            if (button != null)
            {
                // sneaky weirdness required here!
                // see: http://answers.unity3d.com/questions/791573/46-ui-how-to-apply-onclick-handler-for-button-gene.html
                //int buttonNumber = i;
                string fileName = packPath;
                //var e = new MenuButtonEvent(gameObject.name, buttonNumber);
                button.onClick.AddListener(() => { SelectModPack(fileName); });
            }
        }
    }

    private void RefreshCurrentModPack()
    {
        if (currentPack == null)
        {
            currentItemPanel.gameObject.SetActive(false);
            return;
        }

        currentItemPanel.gameObject.SetActive(true);

        submitButtonText.text = "Submit";
        // modPackContents.text = JSON.Dump(currentPack, true);

        RefreshPreview();

        modPackTitle.text = currentPack.title;
        modPackContentFolder.text = currentPack.contentfolder;
        modPackDescription.text = currentPack.description;
        modPackVisibility.value = currentPack.visibility;

        motorsportTag.value = 0;
        eraTag.value = 0;
        foreach (var tagString in currentPack.tags)
        {
            var motorsportTagIndex = motorsportTag.options.FindIndex(t => t.text == tagString);
            if (motorsportTagIndex > -1)
            {
                motorsportTag.value = motorsportTagIndex;
                continue;
            }

            var eraTagIndex = eraTag.options.FindIndex(t => t.text == tagString);
            if (eraTagIndex > -1)
            {
                eraTag.value = eraTagIndex;
                continue;
            }
        }
    }

    private void SelectModPack(string filename)
    {
        if (currentPack != null)
        {
            OnCurrentModPackChanges();
            SaveCurrentModPack();
        }

        var pack = WorkshopModPack.Load(filename);

        if (pack != null)
        {
            currentPack = pack;
            currentPackFilename = filename;

            RefreshCurrentModPack();
            //EditModPack(filename);
        }
    }

    public void RefreshPreview()
    {
        string path = FindHeaderImage();

        if (string.IsNullOrEmpty(path))
        {
            // intentionally set texture to null if no texture is found
            currentPack.previewfile = "";
            modPackPreview.texture = null;
            previewNotFoundPlaceholder.SetActive(true);
            return;
        }

        currentPack.previewfile = Path.GetFileName(path);

        var preview = Utils.LoadTextureFromFile(path);
        modPackPreview.texture = preview;
        previewNotFoundPlaceholder.SetActive(preview == null);
    }

    private bool ValidateModPack(WorkshopModPack pack)
    {
        DisplayAndLogStatus("Validating mod pack...");

        if (!string.IsNullOrEmpty(pack.previewfile))
        {
            string path = Path.Join(basePath, pack.contentfolder, pack.previewfile);

            var info = new FileInfo(path);
            if (info.Length >= 1024 * 1024)
            {
                DisplayAndLogStatus("ERROR: Preview file must be <1MB!");
                return false;
            }
        }

        return true;
    }

    private void OnCurrentModPackChanges()
    {
        OnChanges(currentPack);
        RefreshCurrentModPack();
    }

    private string FindHeaderImage()
    {
        var pngPath = Path.Join(basePath, currentPack.contentfolder, "header_image.png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        var jpgPath = Path.Join(basePath, currentPack.contentfolder, "header_image.jpg");
        if (File.Exists(jpgPath))
        {
            return jpgPath;
        }

        return string.Empty;
    }

    private void OnChanges(WorkshopModPack pack)
    {
        // interface stuff
        pack.title = modPackTitle.text;
        pack.description = modPackDescription.text;
        pack.visibility = modPackVisibility.value;

        pack.tags.Clear();
        if (motorsportTag.value > 0)
        {
            pack.tags.Add(motorsportTag.options[motorsportTag.value].text);
        }

        if (eraTag.value > 0)
        {
            pack.tags.Add(eraTag.options[eraTag.value].text);
        }
    }

    public void AddModPack()
    {
        var packName = modPackName.text;

        // validate modpack name
        if (string.IsNullOrEmpty(packName) || packName.Contains("."))
        {
            DisplayAndLogStatus($"Invalid mod name: {packName}");
        }
        else
        {
            string filename = $"{basePath}{packName}.workshop.json";

            var pack = new WorkshopModPack
            {
                contentfolder = packName,
                title = packName
            };
            pack.Save(filename);

            Directory.CreateDirectory(Path.Join(basePath, modPackName.text));

            RefreshPackList();

            SelectModPack(filename);

            CreateWorkshopItem();
        }
    }

    private void SaveCurrentModPack()
    {
        if (currentPack != null && !string.IsNullOrEmpty(currentPackFilename))
        {
            currentPack.Save(currentPackFilename);
        }
    }

    public void SubmitCurrentModPack()
    {
        if (currentPack != null)
        {
            OnChanges(currentPack);
            SaveCurrentModPack();

            if (ValidateModPack(currentPack))
            {
                if (string.IsNullOrEmpty(currentPack.publishedfileid))
                {
                    CreateWorkshopItem();
                }
                else
                {
                    UploadModPack(currentPack);
                }
            }
        }
    }

    public void BrowseCurrentModPack()
    {
        if (currentPack != null)
        {
            OnChanges(currentPack);
            SaveCurrentModPack();

            var itemPath = Path.Join(basePath, currentPack.contentfolder);
            Debug.Log($"Browsing to {itemPath}");
            Application.OpenURL($"file:///{itemPath}");
        }
    }

    private void CreateWorkshopItem()
    {
        if (string.IsNullOrEmpty(currentPack.publishedfileid))
        {
            SteamAPICall_t call = SteamUGC.CreateItem(new AppId_t(SteamManager.m_consumingAppId),
                                                      EWorkshopFileType.k_EWorkshopFileTypeCommunity);
            itemCreatedCallResult.Set(call, OnItemCreated);

            DisplayAndLogStatus("Creating new item...");
        }
    }

    private void UploadModPack(WorkshopModPack pack)
    {
        if (string.IsNullOrEmpty(currentPack.publishedfileid))
        {
            DisplayAndLogStatus("ERROR: publishedfileid is empty, try creating the workshop item first...");
            return;
        }

        ulong ulongId = ulong.Parse(pack.publishedfileid);
        var id = new PublishedFileId_t(ulongId);

        UGCUpdateHandle_t handle = SteamUGC.StartItemUpdate(new AppId_t(SteamManager.m_consumingAppId), id);
        //m_itemUpdated.Set(call);
        //OnItemUpdated(call, false);

        // Only set the changenotes when clicking submit
        pack.changenote = modPackChangeNotes.text;

        currentHandle = handle;
        SetupModPack(handle, pack);
        SubmitModPack(handle, pack);
    }

    private void SetupModPack(UGCUpdateHandle_t handle, WorkshopModPack pack)
    {
        SteamUGC.SetItemUpdateLanguage(handle, "english");
        SteamUGC.SetItemTitle(handle, pack.title);
        SteamUGC.SetItemDescription(handle, pack.description);
        SteamUGC.SetItemVisibility(handle, (ERemoteStoragePublishedFileVisibility)pack.visibility);
        SteamUGC.SetItemContent(handle, Path.Join(basePath, pack.contentfolder));
        SteamUGC.SetItemPreview(handle,
                                string.IsNullOrEmpty(pack.previewfile)
                                    ? string.Empty
                                    : Path.Join(basePath, pack.contentfolder, pack.previewfile));
        SteamUGC.SetItemMetadata(handle, string.Empty);
        SteamUGC.SetItemTags(handle, pack.tags);
    }

    private void SubmitModPack(UGCUpdateHandle_t handle, WorkshopModPack pack)
    {
        SteamAPICall_t call = SteamUGC.SubmitItemUpdate(handle, pack.changenote);
        itemSubmittedCallResult.Set(call, OnItemSubmitted);
        //In the same way as Creating a Workshop Item, confirm the user has accepted the legal agreement. This is necessary in case where the user didn't initially create the item but is editing an existing item.
    }

    private void OnItemCreated(CreateItemResult_t callback, bool ioFailure)
    {
        if (ioFailure)
        {
            DisplayAndLogStatus("Error: I/O Failure!");
            return;
        }

        switch (callback.m_eResult)
        {
            case EResult.k_EResultInsufficientPrivilege:
                // you're banned!
                DisplayAndLogStatus("Error: Unfortunately, you're banned by the community from uploading to the workshop!");
                break;
            case EResult.k_EResultTimeout:
                DisplayAndLogStatus("Error: Timeout");
                break;
            case EResult.k_EResultNotLoggedOn:
                DisplayAndLogStatus("Error: You're not logged into Steam!\nPlease restart Steam and retry.");
                break;
            case EResult.k_EResultBanned:
                DisplayAndLogStatus("You don't have permission to upload content to this hub because you have an active VAC or Game ban.");
                break;
            case EResult.k_EResultServiceUnavailable:
                DisplayAndLogStatus("The workshop server hosting the content is having issues - please retry.");
                break;
            case EResult.k_EResultInvalidParam:
                DisplayAndLogStatus("One of the submission fields contains something not being accepted by that field.");
                break;
            case EResult.k_EResultAccessDenied:
                DisplayAndLogStatus("There was a problem trying to save the title and description. Access was denied.");
                break;
            case EResult.k_EResultLimitExceeded:
                DisplayAndLogStatus("You have exceeded your Steam Cloud quota. Remove some items and try again.");
                break;
            case EResult.k_EResultFileNotFound:
                DisplayAndLogStatus("The uploaded file could not be found.");
                break;
            case EResult.k_EResultDuplicateRequest:
                DisplayAndLogStatus("The file was already successfully uploaded. Please refresh.");
                break;
            case EResult.k_EResultDuplicateName:
                DisplayAndLogStatus("You already have a Steam Workshop item with that name.");
                break;
            case EResult.k_EResultServiceReadOnly:
                DisplayAndLogStatus("Due to a recent password or email change, you are not allowed to upload new content. Usually this restriction will expire in 5 days, but can last up to 30 days if the account has been inactive recently. ");
                break;
        }

        if (callback.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            DisplayAndLogStatus("You need to accept the Steam Workshop legal agreement for this game before you can upload items!\n" +
                                $"<a href=\"{SteamLegalAgreementUrl}\">{SteamLegalAgreementUrl}</a>", SteamLegalAgreementUrl);
            return;
        }

        if (callback.m_eResult == EResult.k_EResultOK)
        {
            var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={callback.m_nPublishedFileId}";
            DisplayAndLogStatus($"Item creation successful! Published Item ID: {callback.m_nPublishedFileId}\n" +
                                $"<a href=\"{url}\">{url}</a>", url);

            currentPack.publishedfileid = callback.m_nPublishedFileId.ToString();
            /*
            string filename = basePath + modPackName.text + ".workshop.json";

            var pack = new WorkshopModPack();
            pack.publishedfileid = callback.m_nPublishedFileId.ToString();
            pack.Save(filename);

            Directory.CreateDirectory(basePath + modPackName.text);
            
            RefreshPackList();
            */
        }
    }

    private void OnStatusTextClick()
    {
        if (!string.IsNullOrEmpty(statusUrl))
        {
            Application.OpenURL(statusUrl);
        }
    }

    private void SetStatusUrl(string url)
    {
        statusUrl = url;
        statusTextButton.interactable = !string.IsNullOrEmpty(statusUrl);
    }

    private void DisplayAndLogStatus(string status, string urlToOpenOnClick = null)
    {
        // prevent log spamming to the console
        if (lastLoggedString != status || Time.realtimeSinceStartup - lastLogTime > 0.5f)
        {
            statusText.text = status;
            Debug.Log(status);

            lastLoggedString = status;
            lastLogTime = Time.realtimeSinceStartup;

            if (!string.IsNullOrEmpty(urlToOpenOnClick))
            {
                statusUrl = urlToOpenOnClick;
                statusTextButton.interactable = true;
            }
            else
            {
                statusUrl = string.Empty;
                statusTextButton.interactable = false;
            }
        }
    }

    private void OnItemSubmitted(SubmitItemUpdateResult_t callback, bool ioFailure)
    {
        if (ioFailure)
        {
            DisplayAndLogStatus("Error: I/O Failure!");
            return;
        }

        if (callback.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            DisplayAndLogStatus("You need to accept the Steam Workshop legal agreement for this game before you can upload items!\n" +
                                $"<a href=\"{SteamLegalAgreementUrl}\">{SteamLegalAgreementUrl}</a>", SteamLegalAgreementUrl);
            return;
        }

        currentHandle = UGCUpdateHandle_t.Invalid;

        switch (callback.m_eResult)
        {
            case EResult.k_EResultOK:
                var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={callback.m_nPublishedFileId}";
                DisplayAndLogStatus($"Item update successful! Published Item ID: {callback.m_nPublishedFileId}\n" +
                                    $"<a href=\"{url}\">{url}</a>", url);
                break;
            case EResult.k_EResultFail:
                DisplayAndLogStatus("Upload failed.");
                break;
            case EResult.k_EResultInvalidParam:
                DisplayAndLogStatus("Either the provided app ID is invalid or doesn't match the consumer app ID of the item or, you have not enabled ISteamUGC for the provided app ID on the Steam Workshop Configuration App Admin page. The preview file is smaller than 16 bytes.");
                break;
            case EResult.k_EResultAccessDenied:
                DisplayAndLogStatus("ERROR: The user doesn't own a license for the provided app ID.");
                break;
            case EResult.k_EResultFileNotFound:
                DisplayAndLogStatus("Failed to get the workshop info for the item or failed to read the preview file.\nDid you manually delete the item from the workshop?");
                break;
            case EResult.k_EResultLockingFailed:
                DisplayAndLogStatus("Failed to acquire UGC Lock.");
                break;
            case EResult.k_EResultLimitExceeded:
                DisplayAndLogStatus("The preview image is too large, it must be less than 1 Megabyte; or there is not enough space available on the users Steam Cloud.");
                break;
        }
    }

    private void UpdateProgressBar(UGCUpdateHandle_t handle)
    {
        EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(handle, out var bytesDone, out var bytesTotal);

        float progress = (float)bytesDone / bytesTotal;
        progressBar.value = progress;

        switch (status)
        {
            case EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges:
                DisplayAndLogStatus("Committing changes...");
                break;
            case EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile:
                DisplayAndLogStatus("Uploading preview image...");
                break;
            case EItemUpdateStatus.k_EItemUpdateStatusUploadingContent:
                DisplayAndLogStatus("Uploading content...");
                break;
            case EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig:
                DisplayAndLogStatus("Preparing configuration...");
                break;
            case EItemUpdateStatus.k_EItemUpdateStatusPreparingContent:
                DisplayAndLogStatus("Preparing content...");
                break;
            // from the docs: "The item update handle was invalid, the job might be finished, a SubmitItemUpdateResult_t call result should have been returned for it."
            // case EItemUpdateStatus.k_EItemUpdateStatusInvalid:
            //     DisplayAndLogStatus("Item invalid ... dunno why! :(")
            //     break;
        }
    }

    private void Update()
    {
        if (currentHandle != UGCUpdateHandle_t.Invalid)
        {
            UpdateProgressBar(currentHandle);
        }
        else
        {
            progressBar.value = 0f;
        }
    }
}

[System.Serializable]
public class WorkshopModPack
{
    // gets populated when the modpack is loaded; shouldn't be serialized since it would go out of sync
    [Skip] public string filename;

    // populated by the app, should generally be different each time anyways
    [Skip] public string changenote = "Version 1.0";

    // string, because this is a ulong and JSON doesn't like em
    public string publishedfileid = "";
    public string contentfolder = "";
    public string previewfile = "";
    public int visibility = 2; // hidden by default!
    public string title = "New Mod";
    public string description = "Description goes here";
    public string metadata = "";
    public List<string> tags = new List<string>();

    public void ValidateTags()
    {
        var config = Config.Load();

        if (!config.validateTags)
        {
            return;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            // get rid of tags that aren't valid
            if (!config.validTags.Contains(tags[i]))
            {
                Debug.LogError($"Removing invalid tag: {tags[i]}");
                tags.RemoveAt(i);
                i--;
            }
        }
    }

    public static WorkshopModPack Load(string filename)
    {
        WorkshopModPack pack = null;
        string jsonString = Utils.LoadTextFile(filename);
        JSON.MakeInto<WorkshopModPack>(JSON.Load(jsonString), out pack);

        pack.filename = filename;

        return pack;
    }

    public void Save(string saveFilename)
    {
        string jsonString = JSON.Dump(this, true);
        Utils.SaveJsonToFile(saveFilename, jsonString);

        Debug.Log($"Saved modpack to file: {saveFilename}");
    }
}