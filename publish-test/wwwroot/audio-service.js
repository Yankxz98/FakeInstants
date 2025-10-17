// Audio Service for Blazor WebAssembly
console.log('Initializing audio service...');

window.audioService = {
    audioElements: new Map(),
    masterVolume: 1.0,
    dotNetReference: null,

    setDotNetReference: function(dotNetRef) {
        this.dotNetReference = dotNetRef;
    },

    initializeAudio: function(soundId, filePath, volume = 1.0) {
        try {
            // Remove existing audio element if it exists
            if (this.audioElements.has(soundId)) {
                this.audioElements.get(soundId).remove();
            }

            const audio = new Audio(filePath);
            audio.volume = volume * this.masterVolume;
            audio.preload = 'metadata';

            // Handle events
            audio.addEventListener('ended', () => {
                console.log('[AudioService] Audio ended naturally:', soundId);
                this.audioElements.delete(soundId);
                // Notify Blazor that audio has ended
                if (this.dotNetReference) {
                    console.log('[AudioService] Notifying Blazor of audio end:', soundId);
                    this.dotNetReference.invokeMethodAsync('NotifyAudioEnded', soundId)
                        .then(() => console.log('[AudioService] Blazor notified successfully'))
                        .catch(err => console.error('Error notifying audio end:', err));
                }
            });

            audio.addEventListener('error', (e) => {
                console.error('[AudioService] Audio error:', soundId, e);
                this.audioElements.delete(soundId);
                // Also notify on error
                if (this.dotNetReference) {
                    console.log('[AudioService] Notifying Blazor of audio error:', soundId);
                    this.dotNetReference.invokeMethodAsync('NotifyAudioEnded', soundId)
                        .catch(err => console.error('Error notifying audio error:', err));
                }
            });

            this.audioElements.set(soundId, audio);
            return true;
        } catch (error) {
            console.error('Error initializing audio:', error);
            return false;
        }
    },

    playAudio: async function(soundId) {
        try {
            const audio = this.audioElements.get(soundId);
            if (audio) {
                await audio.play();
                return true;
            }
            return false;
        } catch (error) {
            console.error('Error playing audio:', error);
            return false;
        }
    },

    pauseAudio: function(soundId) {
        try {
            const audio = this.audioElements.get(soundId);
            if (audio) {
                audio.pause();
                return true;
            }
            return false;
        } catch (error) {
            console.error('Error pausing audio:', error);
            return false;
        }
    },

    stopAudio: function(soundId) {
        try {
            const audio = this.audioElements.get(soundId);
            if (audio) {
                console.log('[AudioService] Stopping audio manually:', soundId);
                audio.pause();
                audio.currentTime = 0;
                audio.remove();
                this.audioElements.delete(soundId);
                
                // Notify Blazor that audio has stopped
                if (this.dotNetReference) {
                    console.log('[AudioService] Notifying Blazor of manual stop:', soundId);
                    this.dotNetReference.invokeMethodAsync('NotifyAudioEnded', soundId)
                        .then(() => console.log('[AudioService] Blazor notified of stop successfully'))
                        .catch(err => console.error('Error notifying audio stop:', err));
                }
                
                return true;
            }
            return false;
        } catch (error) {
            console.error('Error stopping audio:', error);
            return false;
        }
    },

    getAudioDurationFromBytes: (data) => {
        return new Promise((resolve, reject) => {
            try {
                const blob = new Blob([data], { type: 'audio/mpeg' });
                const url = URL.createObjectURL(blob);

                const audio = new Audio();
                audio.addEventListener('loadedmetadata', () => {
                    URL.revokeObjectURL(url);
                    resolve(audio.duration);
                });
                audio.addEventListener('error', (e) => {
                    URL.revokeObjectURL(url);
                    console.error('Error loading audio from object URL', e);
                    reject('Failed to load audio metadata from data.');
                });
                audio.src = url;
            } catch (err) {
                console.error('Error in getAudioDurationFromBytes', err);
                reject('Error processing audio data.');
            }
        });
    },

    setMasterVolume: function(volume) {
        this.masterVolume = Math.max(0, Math.min(1, volume));

        // Update all active audio elements
        this.audioElements.forEach(audio => {
            audio.volume = this.masterVolume;
        });
    },

    getAudioDuration: function(filePath) {
        return new Promise((resolve, reject) => {
            const audio = new Audio(filePath);
            audio.addEventListener('loadedmetadata', () => {
                resolve(audio.duration);
            });
            audio.addEventListener('error', () => {
                reject(new Error('Failed to load audio metadata'));
            });
        });
    },

    stopAll: function() {
        this.audioElements.forEach((audio, soundId) => {
            audio.pause();
            audio.currentTime = 0;
            audio.remove();
        });
        this.audioElements.clear();
    },

    getSupportedFormats: function() {
        const audio = new Audio();
        return {
            mp3: !!(audio.canPlayType && audio.canPlayType('audio/mpeg;').replace(/no/, '')),
            wav: !!(audio.canPlayType && audio.canPlayType('audio/wav;').replace(/no/, '')),
            ogg: !!(audio.canPlayType && audio.canPlayType('audio/ogg;').replace(/no/, '')),
            aac: !!(audio.canPlayType && audio.canPlayType('audio/aac;').replace(/no/, '')),
            m4a: !!(audio.canPlayType && audio.canPlayType('audio/mp4;').replace(/no/, ''))
        };
    }
};

// Confirm successful initialization
console.log('Audio service initialized successfully');

