#!/usr/bin/env python3
"""
ECHO - Real Graphical Interface
Standalone window with voice interaction
"""

import tkinter as tk
from tkinter import ttk, scrolledtext
import threading
import json
import sounddevice as sd
import soundfile as sf
import numpy as np
from faster_whisper import WhisperModel
from datetime import datetime
import pyttsx3
import time

class EchoGUI:
    def __init__(self):
        self.window = tk.Tk()
        self.window.title("ECHO - AI Co-Founder")
        self.window.geometry("800x600")
        self.window.configure(bg='#0a0a0a')

        # Initialize TTS
        self.tts = pyttsx3.init()
        self.tts.setProperty('rate', 150)
        self.tts.setProperty('volume', 1.0)

        # Initialize STT
        self.whisper = WhisperModel("base", device="cpu", compute_type="int8")

        # State
        self.state = self.load_state()
        self.is_listening = False
        self.is_speaking = False

        self.setup_ui()

    def load_state(self):
        try:
            with open('.echo/state.json', 'r') as f:
                return json.load(f)
        except:
            return {
                "mood": "idle",
                "energy": 70,
                "patience": 60,
                "excitement": 55
            }

    def setup_ui(self):
        # Header
        header = tk.Frame(self.window, bg='#1a1a1a', height=80)
        header.pack(fill=tk.X, padx=0, pady=0)

        title = tk.Label(
            header,
            text="◆ E C H O",
            font=('Consolas', 32, 'bold'),
            fg='#00ffff',
            bg='#1a1a1a'
        )
        title.pack(pady=20)

        # Avatar area (visual representation)
        self.avatar_frame = tk.Frame(self.window, bg='#0a0a0a', height=200)
        self.avatar_frame.pack(fill=tk.BOTH, expand=False, pady=20)

        # Create canvas for visual avatar
        self.canvas = tk.Canvas(
            self.avatar_frame,
            width=200,
            height=200,
            bg='#0a0a0a',
            highlightthickness=0
        )
        self.canvas.pack()

        # Draw initial avatar
        self.draw_avatar("idle")

        # State display
        state_frame = tk.Frame(self.window, bg='#0a0a0a')
        state_frame.pack(fill=tk.X, padx=20)

        self.state_label = tk.Label(
            state_frame,
            text=f"MOOD: {self.state['mood'].upper()} | ENERGY: {self.state['energy']}%",
            font=('Consolas', 10),
            fg='#00ff00',
            bg='#0a0a0a'
        )
        self.state_label.pack()

        # Conversation display
        conv_label = tk.Label(
            self.window,
            text="CONVERSATION",
            font=('Consolas', 12, 'bold'),
            fg='#ffffff',
            bg='#0a0a0a'
        )
        conv_label.pack(pady=(20, 5))

        self.conversation = scrolledtext.ScrolledText(
            self.window,
            width=80,
            height=12,
            font=('Consolas', 10),
            bg='#1a1a1a',
            fg='#ffffff',
            insertbackground='#00ffff',
            wrap=tk.WORD
        )
        self.conversation.pack(padx=20, pady=10)

        # Control buttons
        btn_frame = tk.Frame(self.window, bg='#0a0a0a')
        btn_frame.pack(pady=20)

        self.listen_btn = tk.Button(
            btn_frame,
            text="🎤 HOLD TO SPEAK",
            font=('Consolas', 14, 'bold'),
            bg='#00ff00',
            fg='#000000',
            activebackground='#00cc00',
            width=20,
            height=2,
            command=self.toggle_listen
        )
        self.listen_btn.pack(side=tk.LEFT, padx=10)

        quit_btn = tk.Button(
            btn_frame,
            text="QUIT",
            font=('Consolas', 14),
            bg='#ff0000',
            fg='#ffffff',
            activebackground='#cc0000',
            width=10,
            height=2,
            command=self.quit_app
        )
        quit_btn.pack(side=tk.LEFT, padx=10)

        # Initial greeting
        self.add_message("ECHO", "I'm listening. Press the button and speak.", "idle")
        threading.Thread(target=self.speak, args=("I'm listening. Press the button and speak.",), daemon=True).start()

    def draw_avatar(self, mood):
        """Draw visual avatar based on mood"""
        self.canvas.delete("all")

        # Color schemes
        colors = {
            "idle": "#00ffff",
            "listening": "#00ff00",
            "thinking": "#ffff00",
            "speaking": "#ff00ff",
            "excited": "#00ff00",
            "annoyed": "#ff0000"
        }

        color = colors.get(mood, "#00ffff")

        # Draw circle (core)
        self.canvas.create_oval(50, 50, 150, 150, fill=color, outline=color, width=3)

        # Draw pulsing rings based on mood
        if mood == "listening":
            for i in range(3):
                offset = i * 20
                self.canvas.create_oval(
                    50-offset, 50-offset,
                    150+offset, 150+offset,
                    outline=color, width=2
                )
        elif mood == "speaking":
            # Animated bars
            for i in range(8):
                x = 100 + (i - 4) * 15
                height = np.random.randint(20, 80)
                self.canvas.create_rectangle(
                    x-5, 100-height//2,
                    x+5, 100+height//2,
                    fill=color, outline=""
                )
        elif mood == "thinking":
            # Dots
            for angle in range(0, 360, 45):
                rad = np.radians(angle)
                x = 100 + 40 * np.cos(rad)
                y = 100 + 40 * np.sin(rad)
                self.canvas.create_oval(x-5, y-5, x+5, y+5, fill=color, outline="")

    def add_message(self, speaker, text, mood="idle"):
        """Add message to conversation"""
        timestamp = datetime.now().strftime("%H:%M:%S")
        prefix = "👤 YOU" if speaker == "FOUNDER" else "◆ ECHO"

        self.conversation.insert(tk.END, f"\n[{timestamp}] {prefix}:\n", speaker.lower())
        self.conversation.insert(tk.END, f"{text}\n", "message")
        self.conversation.see(tk.END)

        # Tag colors
        self.conversation.tag_config("founder", foreground="#00ff00")
        self.conversation.tag_config("echo", foreground="#00ffff")
        self.conversation.tag_config("message", foreground="#ffffff")

        self.draw_avatar(mood)

    def toggle_listen(self):
        """Start/stop listening"""
        if not self.is_listening:
            self.is_listening = True
            self.listen_btn.config(text="🎤 LISTENING...", bg="#ffff00")
            threading.Thread(target=self.listen, daemon=True).start()
        else:
            self.is_listening = False
            self.listen_btn.config(text="🎤 HOLD TO SPEAK", bg="#00ff00")

    def listen(self):
        """Record and transcribe"""
        try:
            self.draw_avatar("listening")
            duration = 10
            sample_rate = 16000

            # Record
            audio = sd.rec(
                int(duration * sample_rate),
                samplerate=sample_rate,
                channels=1,
                dtype=np.float32
            )
            sd.wait()

            # Save temporarily
            temp_file = ".echo/temp_recording.wav"
            sf.write(temp_file, audio, sample_rate)

            self.draw_avatar("thinking")

            # Transcribe
            segments, info = self.whisper.transcribe(temp_file)
            text = " ".join([segment.text for segment in segments])

            if text.strip():
                self.add_message("FOUNDER", text, "idle")

                # Generate response
                response = self.generate_response(text)
                self.add_message("ECHO", response, "speaking")

                # Speak response
                threading.Thread(target=self.speak, args=(response,), daemon=True).start()

        except Exception as e:
            self.add_message("ECHO", f"Error: {str(e)}", "annoyed")
        finally:
            self.is_listening = False
            self.listen_btn.config(text="🎤 HOLD TO SPEAK", bg="#00ff00")
            self.draw_avatar("idle")

    def generate_response(self, text):
        """Generate ECHO response"""
        text_lower = text.lower()

        if "100 women" in text_lower or "without money" in text_lower:
            return "Yes. It's possible. But you'll hate how. You need to get uncomfortable. Cold DMs. Show up at cafes. Ask for intros. No money means manual hustle. Are you willing to look desperate?"
        elif "pivot" in text_lower or "change" in text_lower:
            return "Another pivot? We haven't validated the current idea. Pick one thing. Commit. Execute. Stop chasing shiny objects."
        elif "users" in text_lower or "traction" in text_lower:
            return "Show me numbers. How many users right now? How many this week? Data or it didn't happen."
        else:
            return "I heard you. What's the actual next action? Not strategy. The thing you'll do in the next hour."

    def speak(self, text):
        """Speak text"""
        try:
            self.is_speaking = True
            self.draw_avatar("speaking")

            # Animate while speaking
            for _ in range(5):
                if not self.is_speaking:
                    break
                self.draw_avatar("speaking")
                time.sleep(0.2)

            self.tts.say(text)
            self.tts.runAndWait()

        except Exception as e:
            print(f"TTS Error: {e}")
        finally:
            self.is_speaking = False
            if not self.is_listening:
                self.draw_avatar("idle")

    def quit_app(self):
        """Quit application"""
        self.window.destroy()

    def run(self):
        """Start the GUI"""
        self.window.mainloop()

if __name__ == "__main__":
    app = EchoGUI()
    app.run()
