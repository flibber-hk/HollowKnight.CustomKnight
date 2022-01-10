using System;
using System.IO;
using System.Linq;

using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;           

using GlobalEnums;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using Satchel;
using static Satchel.IoUtils;

namespace CustomKnight {
    public class SwapManager
    {
        private string DATA_DIR;
        internal string SWAP_FOLDER = "Swap";
        private string SEPARATOR = "=>";
        internal float BACKOFF_MULTIPLIER = 1.3f;
        internal int INITAL_NEXT_CHECK = 1000;

        internal int nextCheck;
        internal Dictionary<string,Dictionary<string,GameObjectProxy>> Scenes;
        internal List<string> currentSkinnedSceneObjs;
        internal Dictionary<string,Texture2D> loadedTextures;

        internal Dictionary<string,Material> materials;
        internal Dictionary<string,Texture2D> defaultTextures;

        internal Dictionary<string,string> Strings;
        internal Dictionary<string,string> ReplaceStrings;
        internal Dictionary<string,List<string>> ReplaceCache;

        internal DateTime lastTime = DateTime.Now;

        internal bool active = false;
        internal bool enabled = false;
        public SwapManager(){
                Load();
        }
       
        internal bool SwapSkinRoutineRunning = false;

        private void loadTexture(GameObjectProxy gop){
                string objectPath = gop.getTexturePath();
                if(loadedTextures.TryGetValue(objectPath, out var tex)){
                    return;
                }
                byte[] buffer;
                string defaultDirectory = Path.Combine(SkinManager.DATA_DIR,SWAP_FOLDER);
                string currentDirectory = DATA_DIR;
                if(File.Exists(Path.Combine(currentDirectory,objectPath))){
                    buffer = File.ReadAllBytes(Path.Combine(currentDirectory,objectPath));
                } else if(File.Exists(Path.Combine(defaultDirectory,objectPath))){
                    buffer = File.ReadAllBytes(Path.Combine(defaultDirectory,objectPath));
                } else {
                    return;
                }
                
                var texture = new Texture2D(2, 2);
                texture.LoadImage(buffer.ToArray(),true);
                loadedTextures[objectPath] = texture;
        }

        private void SwapSkinForGo(string objectPath,GameObject GO){
            Texture2D tex = loadedTextures[objectPath];
            var _tk2dSprite = GO.GetComponent<tk2dSprite>();
            if(_tk2dSprite == null){
                var anim = GO.GetComponent<Animator>();
                var sr = GO.GetComponent<SpriteRenderer>();
                if(false && anim != null && sr != null){
                    //maybe animates
                    var caf = GO.GetAddComponent<CustomAnimationFrames>();
                    var filename = Path.GetFileName(objectPath);
                    var splitName = filename.Split('.');
                    var pivot = new Vector2(0.5f, 0.5f); // this needs offset sometimes
                    var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot ,64f);
                    caf.Add(splitName[0],int.Parse(splitName[1]),spr);
                    return;
                }
                //assume sprite
                if(sr == null){
                    this.Log("No tk2dSprite or SpriteRenderer Component found in " + objectPath);
                } else {
                    //currentSkinnedSceneObjs.Add(objectPath); re add sprites for a while
                    //some sprites are still not perfectly matched 
                    CustomKnight.Instance.Log($"game object : {sr.name} ");
                    var pivot = new Vector2(0.5f, 0.5f); // this needs offset sometimes
                    sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot ,sr.sprite.pixelsPerUnit);
                    //Log($"pivot post application {sr.sprite.pivot/new Vector2(tex.width, tex.height)}");
                }

            } else {
                currentSkinnedSceneObjs.Add(objectPath);
                materials[objectPath] = _tk2dSprite.GetCurrentSpriteDef().material;
                defaultTextures[objectPath] = (Texture2D) materials[objectPath].mainTexture;
                _tk2dSprite.GetCurrentSpriteDef().material.mainTexture = tex;
            }
        }
        
        private void applySkinsUsingProxy(GameObjectProxy gop,GameObject go){
            //CustomKnight.Instance.Log("Traversing : " + gop.getTexturePath());
            if(go == null){
                CustomKnight.Instance.Log("Null Go : " + gop.getTexturePath());
                return;
            }
            if(gop.hasTexture){
                //CustomKnight.Instance.Log("hasTexture");
                    try{
                        loadTexture(gop);
                    } catch( Exception e){
                        this.Log( gop.name + " " + e.ToString());
                    }
                if(!currentSkinnedSceneObjs.Contains(gop.getTexturePath())){
                    SwapSkinForGo(gop.getTexturePath(),go);
                }
            }
            //traverse this gop
            if(gop.hasChildren){
                //CustomKnight.Instance.Log("hasChildren " + gop.children.Count());
                foreach(KeyValuePair<string,GameObjectProxy> kvp in gop.children){
                    try{
                        this.Log(kvp.Key);
                        var children = go.FindGameObjectsInChildren(kvp.Key,true);
                        foreach(var child in children){
                            applySkinsUsingProxy(kvp.Value,child);
                        }
                    } catch( Exception e){
                        this.Log( kvp.Key + " " + e.ToString());
                    }
                }
            }
        }
        private void SwapSkinForAllScenes(){
           var scenes = SceneUtils.GetAllLoadedScenes();
           foreach(var scene in scenes){ 
                SwapSkin(scene);
           }
        }
        private void SwapSkin(Scene scene){
            if (Scenes != null && Scenes.TryGetValue(scene.name, out var CurrentSceneDict))
            {
                var rootGos = scene.GetRootGameObjects();
                /*foreach(KeyValuePair<string,GameObjectProxy> kvp in CurrentSceneDict){
                    Log($"={kvp.Key}");
                }*/
                foreach(var go in rootGos){
                    if(CurrentSceneDict.TryGetValue(go.GetName(true),out var gop)){
                        //Log($"+{go.GetName(true)}");
                        applySkinsUsingProxy(gop,go);
                    }
                }
            } 
        }
        private IEnumerator SwapSkinRoutine(Scene scene){
            SwapSkinRoutineRunning = true;
            yield return null;
            SwapSkinForAllScenes();
            SwapSkinRoutineRunning = false;
        }
        internal void SwapSkinForScene(Scene scene,LoadSceneMode mode){
            if(!active && !enabled) {return;}
            currentSkinnedSceneObjs = new List<string>();
            nextCheck = INITAL_NEXT_CHECK;
            GameManager.instance.StartCoroutine(SwapSkinRoutine(scene));
        }

        internal void checkForMissedObjects(){
            if(!active && !enabled) {return;}
            var currentTime = DateTime.Now;
            if(nextCheck > 0 && (currentTime - lastTime).TotalMilliseconds > nextCheck){
                SwapSkinForAllScenes();
                nextCheck = (int)Math.Round((float)nextCheck * BACKOFF_MULTIPLIER);
                lastTime = currentTime;
            }
        }

        internal string LanguageGet( string key, string sheet , string orig ){
            if(!enabled && !active){ 
                return orig;
            }
            string overrideText;
            if(Strings != null && Strings.TryGetValue(sheet+key, out overrideText)){
                return overrideText;
            }
            if(ReplaceCache != null && ReplaceCache.TryGetValue(sheet+key, out var cachedText)){
                if(cachedText[0] == orig){ // only consider it valid cache if orig matches first element of list
                    return cachedText[1];
                }
            }
            string textValue = orig;
            if(ReplaceStrings !=null) {
                foreach( KeyValuePair<string,string> kp in ReplaceStrings){
                    textValue = Regex.Replace(textValue, Regex.Escape(kp.Key), kp.Value.Replace("$","$$"), RegexOptions.IgnoreCase);
                }
                //cache for next time
                ReplaceCache[sheet+key]=new List<string>{orig,textValue};
            }
            return textValue;
        }

        internal void LoadSwapByPath(string pathToLoad){
            if (!File.Exists(Path.Combine(pathToLoad,"replace.txt")))
            {
                EnsureDirectory(pathToLoad);
                using (FileStream fs = File.Create(Path.Combine(pathToLoad,"replace.txt"))){
                    //create and close the stream
                };
            }
            using(StreamReader reader = File.OpenText(Path.Combine(pathToLoad,"replace.txt")))
            {
                while (!reader.EndOfStream)
                {
                   string line = reader.ReadLine();
                   Log(line);
                   int index = line.IndexOf(SEPARATOR);
                   if(index > 0 && index < line.Length - 1)
                   {
                       string replace = line.Substring(0, index).ToLower();
                       string with = line.Substring(index + SEPARATOR.Length);
                       
                       ReplaceStrings[replace]=with;
                   }
                }
            }
            
            foreach (string path in Directory.GetDirectories(pathToLoad))
            {
                string directoryName = new DirectoryInfo(path).Name;
                //Log(directoryName);
                Dictionary<string,GameObjectProxy> objects;
                if(!Scenes.TryGetValue(directoryName, out objects)){
                    objects = new Dictionary<string,GameObjectProxy>();
                }
                foreach(string file in Directory.GetFiles(path)){
                    string filename = Path.GetFileName(file);
                    //Log(filename);
                    if(filename.EndsWith(".txt")){
                       try{
                           Strings[filename.Replace(".txt","")] = File.ReadAllText(file);
                       } catch( Exception e){
                           this.Log( filename + " " + e.ToString());
                           continue;
                       }
                    }
                    if(filename.EndsWith(".png")){
                        string objectName = filename.Replace(".png","");
                        GameObjectProxy GOP = new GameObjectProxy(){
                            name = objectName,
                            hasTexture = true,
                            rootPath = directoryName,
                            hasChildren = false
                        };
                        objects.Add(objectName,GOP);
                    }
                }
                foreach(string childDirectory in Directory.GetDirectories(path)){
                    string childDirectoryName = new DirectoryInfo(childDirectory).Name;
                    Log(childDirectoryName);
                    GameObjectProxy GOP;
                    if(!objects.TryGetValue(childDirectoryName,out GOP)){
                        GOP = new GameObjectProxy(){
                            name = childDirectoryName,
                            hasTexture = false,
                            rootPath = directoryName,
                            hasChildren = true
                        };
                        objects.Add(childDirectoryName,GOP);
                    }
                    GOP.TraverseGameObjectDirectory(pathToLoad);
                }
                Scenes[directoryName] = objects;
            }
        }
        
        internal void Swap(string skinpath)
        {

            Scenes = new Dictionary<string,Dictionary<string,GameObjectProxy>>();
            currentSkinnedSceneObjs = new List<string>();
            loadedTextures = new Dictionary<string, Texture2D>();

            materials = new Dictionary<string, Material>();
            defaultTextures = new Dictionary<string, Texture2D>();

            Strings  = new Dictionary<string,string>();         
            ReplaceStrings  = new Dictionary<string,string>();
            ReplaceCache = new Dictionary<string, List<string>>();   
            nextCheck = INITAL_NEXT_CHECK;

            LoadSwapByPath(Path.Combine(SkinManager.DATA_DIR,SWAP_FOLDER)); // global strings and skins

            DATA_DIR = Path.Combine(skinpath,SWAP_FOLDER);
            
            EnsureDirectory(DATA_DIR);

            LoadSwapByPath(DATA_DIR); // over write global strings with local strings 

            if (Directory.GetDirectories(DATA_DIR).Length == 0)
            {
                Log("There are no folders in the Swap directory. No textures to Swap.");
                return;
            }
            GameManager.instance.StartCoroutine(SwapSkinRoutine(UnityEngine.SceneManagement.SceneManager.GetActiveScene()));
        }

        internal void resetAllTextures(){
            if(materials != null){
                foreach(KeyValuePair<string,Material> kp in materials){
                    if(kp.Value == null) {
                        continue;
                    }
                    kp.Value.mainTexture =  defaultTextures[kp.Key];
                }      
            }
        }
        internal void Load(){
            ModHooks.LanguageGetHook += LanguageGet;
            ModHooks.HeroUpdateHook += checkForMissedObjects;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += SwapSkinForScene;
            On.HutongGames.PlayMaker.Actions.ActivateGameObject.DoActivateGameObject += ActivateGameObject;
        }

        internal void Unload(){
            ModHooks.LanguageGetHook -= LanguageGet;
            ModHooks.HeroUpdateHook -= checkForMissedObjects;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= SwapSkinForScene;
            On.HutongGames.PlayMaker.Actions.ActivateGameObject.DoActivateGameObject -= ActivateGameObject;
            resetAllTextures();
        }
 
        internal void ActivateGameObject(On.HutongGames.PlayMaker.Actions.ActivateGameObject.orig_DoActivateGameObject orig, HutongGames.PlayMaker.Actions.ActivateGameObject self){
            orig(self);
            if(!active && !enabled) {return;}
            if(self.activate.Value != true) {return;}
            if(!SwapSkinRoutineRunning){
                GameManager.instance.StartCoroutine(SwapSkinRoutine(UnityEngine.SceneManagement.SceneManager.GetActiveScene()));
            }
        }

        internal void Log(string str) {
            CustomKnight.Instance.Log("[SwapManager] " +str);
        }

    }
}