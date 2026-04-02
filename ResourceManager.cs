using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;

namespace CardGame {
    internal static class ResourceManager {
        /// <summary>
        /// Specifies the maximum number of atlases that can be used per fontsystem.
        /// </summary>
        public static byte MaxAtlasCount = 3;
        public static bool IsLoaded { get; private set; } = false;
        private static long loadedResources = 0;
        private static long totalResources = 0;
        private static bool extCalledTextureLoading = false;

        /// <summary>
        /// Determines the expected frame rate for the application, which is used to calculate the batch size for loading textures externally with the <see cref="LoadNextTextureBatch(ContentManager)"/> method.
        /// </summary>
        /// <remarks> Default value is 60. </remarks>
        public static int ExceptedFrameRate
        {
            get => exceptedFrameRate;
            set {
                if (value <= 0) {
                    throw new ArgumentOutOfRangeException(nameof(ExceptedFrameRate), "Excepted frame rate must be greater than zero.");
                }
                exceptedFrameRate = value;
                batchSize = 0;
            }
        }
        private static int exceptedFrameRate = 60;

        /// <summary>
        /// Determines the target load time for loading resources externally, with the <see cref="LoadNextTextureBatch(ContentManager)"/> method.
        /// This value represents the desired time in seconds that the whole batch of textures should take to load when using external loading.
        /// Adjusting this value can help balance loading performance and responsiveness, allowing for smoother loading experiences by controlling how much work is done in each batch.
        /// </summary>
        /// <remarks> Default value is 10 seconds. </remarks>
        public static int TargetLoadTime
        {
            get => targetLoadTime;
            set {
                if (value <= 0) {
                    throw new ArgumentOutOfRangeException(nameof(TargetLoadTime), "Target load time must be greater than zero.");
                }
                targetLoadTime = value;
                batchSize = 0;
            }
        }
        private static int targetLoadTime = 10;
        private static int batchSize = 0;

        /// <summary>
        /// Gets or sets the file path to the texture resources. Relative to the Content root directory.
        /// </summary>
        public static string TexturePath { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the file path of the songs. Relative to the Content root directory.
        /// </summary>
        public static string SongPath { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the file path to the sound resources. Relative to the Content root directory.
        /// </summary>
        public static string SoundPath { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the file path to the font resources. Relative to the Content root directory.
        /// </summary>
        public static string FontPath { get; set; } = string.Empty;
        /// <summary>
        /// Gets the total number of resources currently available for the application. Including textures, sound effects, songs, and fonts.
        /// </summary>
        public static Int64 TotalResources
        {
            get => Interlocked.Read(ref totalResources);
            private set => Interlocked.Exchange(ref totalResources, value);
        }
        /// <summary>
        /// Gets the total number of resources that have been loaded by the application.
        /// </summary>
        public static Int64 LoadedResources
        {
            get => Interlocked.Read(ref loadedResources);
            private set => Interlocked.Exchange(ref loadedResources, value);
        }
        private static readonly HashSet<FontSystem> toReset = [];

        /// <summary>
        /// Gets the collection of textures grouped by their associated keys. The key is derived from the texture file name without its extension and any trailing digits or numbering.
        /// Returns a Texture2D array. If a texture is part of a sequence (e.g., texture1, texture2, texture3), all related textures are grouped into an array under the same key.
        /// </summary>
        public static FrozenDictionary<string, Texture2D[]> Textures { get; private set; }
        private static readonly Queue<Tuple<string, string[], List<Texture2D>>> extTextureLoadQueue = new();
        private static List<Tuple<string, string[], List<Texture2D>>> extTextureLoadDone;
        private static readonly Dictionary<Color, Texture2D> SingleColorTextures = new(10);
        /// <summary>
        /// Gets the collection of sound effects available in the application, indexed by unique string keys. The key is derived from the sound effect file name without its extension.
        /// </summary>
        public static FrozenDictionary<string, SoundEffect> SoundEffects { get; private set; }
        /// <summary>
        /// Gets the collection of songs, indexed by unique string keys. The key is derived from the song file name without its extension.
        /// </summary>
        public static FrozenDictionary<string, Song> Songs { get; private set; }
        /// <summary>
        /// Gets the collection of fonts available for use, indexed by unique string keys. The key is derived from the font file name without its extension.
        /// </summary>
        public static FrozenDictionary<string, FontSystem> Fonts { get; private set; }


        /// <summary>
        /// Initializes the content manager and loads textures, songs, sound effects, and fonts from the specified
        /// paths.
        /// </summary>
        /// <remarks>This method initializes the asset dictionaries for textures, songs, sound effects,
        /// and fonts as immutable collections. It scans the specified directories for assets, ensuring that asset names
        /// are unique. If a path is not provided for a specific asset type, the previously set path will be
        /// used.</remarks>
        /// <param name="Content">The content manager used to load assets.</param>
        /// <param name="texturepath">The relative path to the directory containing texture assets. If <paramref name="texturepath"/> is null, the
        /// previously set texture path will be used. Throws an exception if no texture path has been set.</param>
        /// <param name="songpath">The relative path to the directory containing song assets. If null, the previously set song path will be
        /// used. If 'string.Empty' or its originally was 'string.Empty' and now it is null, then songs wont be loaded.</param>
        /// <param name="soundpath">The relative path to the directory containing sound effect assets. If null, the previously set sound path
        /// will be used. If 'string.Empty' or its originally was 'string.Empty' and now it is null, then sound effects wont be loaded.</param>
        /// <param name="fontpath">The relative path to the directory containing font assets. If null, the previously set font path will be
        /// used. If 'string.Empty' or its originally was 'string.Empty' and now it is null, then fonts wont be loaded.</param>
        /// <param name="externallyCalledTextureLoading">Indicates whether the texture loading is being handled externally. 
        /// If true, the method will not load textures or set the IsLoaded flag to true. Textures can be loaded in steps by another externally callable method.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="texturepath"/> is null and no texture path has been set previously.</exception>
        /// <exception cref="Exception">Thrown if duplicate keys are detected for textures, songs, sound effects, or fonts. Asset names must be
        /// unique.</exception>
        public static void Init(ContentManager Content, string texturepath = null, string songpath = null, string soundpath = null, string fontpath = null, bool externallyCalledTextureLoading = false)
        {
            if (texturepath is null && TexturePath == string.Empty) {
                throw new ArgumentNullException(nameof(texturepath), "Texture path cannot be null if it has not been set before.");
            }
            FontSystemDefaults.TextureWidth = 2048;
            FontSystemDefaults.TextureHeight = 2048;
            FontSystemDefaults.FontResolutionFactor = 2.0f;
            FontSystemDefaults.KernelWidth = 2;
            FontSystemDefaults.KernelHeight = 2;
            FontSystemDefaults.GlyphRenderResult = GlyphRenderResult.NonPremultiplied;
            FontSystemDefaults.UseKernings = true;
            TexturePath = texturepath ?? TexturePath;
            SongPath = songpath ?? SongPath;
            SoundPath = soundpath ?? SoundPath;
            FontPath = fontpath ?? FontPath;
            string currentpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Content.RootDirectory);
            // Load Textures
            string currentpath_T = Path.Combine(currentpath, TexturePath);
            string[] filesT = Directory.GetFiles(currentpath_T, "*.xnb", SearchOption.AllDirectories);
            string currentpath_F = Path.Combine(currentpath, FontPath);
            string[] filesF = Directory.GetFiles(currentpath_F, "*.*", SearchOption.AllDirectories);
            string currentpath_S = Path.Combine(currentpath, SongPath);
            string[] filesS = Directory.GetFiles(currentpath_S, "*.xnb", SearchOption.AllDirectories);
            string currentpath_E = Path.Combine(currentpath, SoundPath);
            string[] filesSFX = Directory.GetFiles(currentpath_E, "*.xnb", SearchOption.AllDirectories);
            Interlocked.Add(ref totalResources, filesT.Length); //set resource count
            Interlocked.Add(ref totalResources, filesF.Length);
            Interlocked.Add(ref totalResources, filesS.Length);
            Interlocked.Add(ref totalResources, filesSFX.Length);
            Dictionary<string, Texture2D[]> textures = new(filesT.Length);
            if (externallyCalledTextureLoading)
                extTextureLoadDone = new(filesT.Length);
            Dictionary<string, SoundEffect> soundeffects = new(filesSFX.Length);
            Dictionary<string, Song> songs = new(filesS.Length);
            Dictionary<string, FontSystem> fonts = new(filesF.Length);
            List<string> piplinepaths = [];
            foreach (string file in filesT) {
                string relativePath = Path.GetRelativePath(currentpath, file);
                string pipelinepath = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                piplinepaths.Add(pipelinepath);
            }
            Dictionary<string, List<Tuple<string, int>>> texture_sequences = [];
            for (int i = piplinepaths.Count - 1; i > -1; i--) {
                if (Char.IsDigit(piplinepaths[i].Last())) {
                    int numlength = 1;
                    for (int j = piplinepaths[i].Length - 2; j > -1; j--) {
                        if (Char.IsDigit(piplinepaths[i][j])) {
                            numlength++;
                        }
                        else {
                            break;
                        }
                    }
                    string key = piplinepaths[i].Substring(0, piplinepaths[i].Length - numlength);
                    int num = int.Parse(piplinepaths[i].Substring(piplinepaths[i].Length - numlength));
                    if (texture_sequences.TryGetValue(key, out List<Tuple<string, int>> value)) {
                        value.Add(new(piplinepaths[i], num));
                    }
                    else {
                        texture_sequences.Add(key, [new(piplinepaths[i], num)]);
                    }
                    piplinepaths.RemoveAt(i);
                }
            }
            HashSet<string> duplicateCheck = new(texture_sequences.Keys.Count);
            foreach (string key in texture_sequences.Keys) {
                List<Tuple<string, int>> sequence = texture_sequences[key];
                sequence.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                string newkey = key.Split("/").Last();
                if (!externallyCalledTextureLoading) {
                    List<Texture2D> textureseq = [];
                    foreach (Tuple<string, int> item in sequence) {
                        textureseq.Add(Content.Load<Texture2D>(item.Item1));
                        Interlocked.Increment(ref loadedResources);
                    }
                    if (!textures.ContainsKey(newkey)) {
                        textures.Add(newkey, textureseq.ToArray());
                    }
                    else {
                        throw new Exception($"Duplicate texture key: {newkey} ! Texture names MUST be unique!");
                    }
                }
                else {
                    if (duplicateCheck.Contains(newkey))
                        throw new Exception($"Duplicate texture key: {newkey} ! Texture names MUST be unique!");
                    extTextureLoadQueue.Enqueue(new(newkey, sequence.Select(t => t.Item1).ToArray(), new(sequence.Count)));
                    duplicateCheck.Add(newkey);
                }
            }
            foreach (string path in piplinepaths) {
                string key = path.Split("/").Last();
                if (!externallyCalledTextureLoading) {
                    if (!textures.ContainsKey(key)) {
                        textures.Add(key, [Content.Load<Texture2D>(path)]);
                        Interlocked.Increment(ref loadedResources);
                    }
                    else {
                        throw new Exception($"Duplicate texture key: {key} ! Texture names MUST be unique!");
                    }
                }
                else {
                    if (duplicateCheck.Contains(key))
                        throw new Exception($"Duplicate texture key: {key} ! Texture names MUST be unique!");
                    extTextureLoadQueue.Enqueue(new(key, [path], new(1)));
                    duplicateCheck.Add(key);
                }
            }
            if (!externallyCalledTextureLoading)
                Textures = textures.ToFrozenDictionary();
            else
                extCalledTextureLoading = true;
            // Load Songs
            if (SongPath != string.Empty) {
                piplinepaths.Clear();
                foreach (string file in filesS) {
                    string relativePath = Path.GetRelativePath(currentpath, file);
                    string pipelinepath = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                    piplinepaths.Add(pipelinepath);
                }
                foreach (string path in piplinepaths) {
                    string key = path.Split("/").Last();
                    if (!songs.ContainsKey(key)) {
                        songs.Add(key, Content.Load<Song>(path));
                        Interlocked.Increment(ref loadedResources);
                    }
                    else {
                        throw new Exception($"Duplicate song key: {key} ! Song names MUST be unique!");
                    }
                }
                Songs = songs.ToFrozenDictionary();
            }
            // Load SoundEffects
            if (SoundPath != string.Empty) {
                piplinepaths.Clear();
                foreach (string file in filesSFX) {
                    string relativePath = Path.GetRelativePath(currentpath, file);
                    string pipelinepath = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, '/');
                    piplinepaths.Add(pipelinepath);
                }
                foreach (string path in piplinepaths) {
                    string key = path.Split("/").Last();
                    if (!soundeffects.ContainsKey(key)) {
                        soundeffects.Add(key, Content.Load<SoundEffect>(path));
                        Interlocked.Increment(ref loadedResources);
                    }
                    else {
                        throw new Exception($"Duplicate sound effect key: {key} ! Sound effect names MUST be unique!");
                    }
                }
                SoundEffects = soundeffects.ToFrozenDictionary();
            }
            // Load Fonts
            if (FontPath != string.Empty) {
                piplinepaths.Clear();
                List<string> relativepaths = [];
                foreach (string file in filesF) {
                    string relativePath = Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
                    string pipelinepath = Path.ChangeExtension(relativePath, null).Split("/").Last();
                    relativepaths.Add(relativePath);
                    piplinepaths.Add(pipelinepath);
                }
                for (int i = 0; i < piplinepaths.Count; i++) {
                    if (!fonts.ContainsKey(piplinepaths[i])) {
                        FontSystem fontSystem = new();
                        fontSystem.CurrentAtlasFull += ClearFonts;
                        fontSystem.AddFont(File.ReadAllBytes(relativepaths[i]));
                        fonts.Add(piplinepaths[i], fontSystem);
                        Interlocked.Increment(ref loadedResources);
                    }
                    else {
                        throw new Exception($"Duplicate font key: {piplinepaths[i]} ! Font names MUST be unique!");
                    }
                }
                Fonts = fonts.ToFrozenDictionary();
            }
            IsLoaded = !externallyCalledTextureLoading;
        }

        /// <summary>
        /// If texture loading is being handled externally, this method loads the next batch of textures from the queue.
        /// The batch size is determined by the expected frame rate and target load time, allowing for smoother loading experiences
        /// by controlling how much work is done in each batch. Once all textures have been loaded, the method finalizes the texture
        /// dictionary and sets the IsLoaded flag to true.
        /// </summary>
        /// <param name="Content"> Monogame ContentManager </param>
        public static void LoadNextTextureBatch(ContentManager Content)
        {
            if (!extCalledTextureLoading || IsLoaded)
                return;
            if (extTextureLoadQueue.Count == 0) {
                Textures = extTextureLoadDone.ToFrozenDictionary(batch => batch.Item1, batch => batch.Item3.ToArray());
                extTextureLoadDone = null;
                IsLoaded = true;
                return;
            }
            if (batchSize == 0) {
                batchSize = (int)Math.Ceiling((double)extTextureLoadQueue.Sum(batch => batch.Item2.Length) / ExceptedFrameRate / TargetLoadTime);
            }
            int loadedCount = 0;
            while (loadedCount < batchSize && extTextureLoadQueue.Count > 0) {
                Tuple<string, string[], List<Texture2D>> batch = extTextureLoadQueue.Dequeue();
                for (int i = batch.Item3.Count; i < batch.Item2.Length; i++) {
                    batch.Item3.Add(Content.Load<Texture2D>(batch.Item2[i]));
                    Interlocked.Increment(ref loadedResources);
                    loadedCount++;
                    if (loadedCount >= batchSize) {
                        break;
                    }
                }
                if (batch.Item3.Count == batch.Item2.Length) {
                    extTextureLoadDone.Add(batch);
                }
                else {
                    extTextureLoadQueue.Enqueue(batch);
                }
            }
        }

        private static void ClearFonts(object sender, EventArgs e)
        {
            FontSystem fontSystem = (FontSystem)sender;
            if (fontSystem.Atlases.Count >= MaxAtlasCount) {
                toReset.Add(fontSystem);
            }
        }

        public static void ResetFonts()
        {
            foreach (FontSystem fontSystem in toReset) {
                fontSystem.Reset();
            }
            toReset.Clear();
        }

        /// <summary>
        /// Returns a 1x1 texture filled with the specified color, creating and caching it if necessary.
        /// </summary>
        /// <remarks>The returned texture is cached for reuse. Repeated calls with the same color will
        /// return the same Texture2D instance, which can improve performance and reduce memory usage.</remarks>
        /// <param name="color">The color to fill the texture with.</param>
        /// <param name="spriteBatch">The sprite batch whose graphics device is used to create the texture.</param>
        /// <returns>A 1x1 Texture2D instance filled with the specified color. If a texture with the given color has already been
        /// created, the cached instance is returned.</returns>
        public static Texture2D GetColor(Color color, SpriteBatch spriteBatch)
        {
            if (SingleColorTextures.TryGetValue(color, out Texture2D tex)) {
                return tex;
            }
            Texture2D newTex = new(spriteBatch.GraphicsDevice, 1, 1);
            newTex.SetData([color]);
            SingleColorTextures.Add(color, newTex);
            return newTex;
        }

        /// <summary>
        /// Calculates the overall progress of resource loading as a fractional value between 0.0 and 1.0.
        /// </summary>
        /// <returns>A double value representing the proportion of resources that have been loaded. Returns 1.0 if there are no
        /// resources to load.</returns>
        public static double GetLoadProgress() => TotalResources == 0 ? 0.0 : (double)LoadedResources / (double)TotalResources;

        public static void Dispose()
        {
            foreach (Texture2D tex in SingleColorTextures.Values) {
                tex.Dispose();
            }
            SingleColorTextures.Clear();
            foreach (FontSystem font in Fonts.Values) {
                font.Reset();
                font.Dispose();
            }
            foreach (Texture2D[] texArray in Textures.Values) {
                foreach (Texture2D tex in texArray) {
                    tex.Dispose();
                }
            }
            foreach (SoundEffect sfx in SoundEffects.Values) {
                sfx.Dispose();
            }
        }
    }
}
