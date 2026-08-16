#!/usr/bin/env python3
"""Test microphone - see if it's working"""

import sounddevice as sd
import numpy as np
import time

print("=" * 60)
print("MICROPHONE TEST")
print("=" * 60)
print("\nAvailable audio devices:")
print(sd.query_devices())
print("\n" + "=" * 60)

print("\nTesting microphone for 10 seconds...")
print("SPEAK NOW - you should see bars when you talk:")
print()

def callback(indata, frames, time_info, status):
    volume = np.linalg.norm(indata) * 10
    bars = int(volume)
    print('|' * min(bars, 60), f" {volume:.3f}")

stream = sd.InputStream(callback=callback, channels=1, samplerate=16000)

with stream:
    time.sleep(10)

print("\nTest complete!")
