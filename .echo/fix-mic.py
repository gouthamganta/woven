#!/usr/bin/env python3
"""Fix microphone - test specific device"""

import sounddevice as sd
import numpy as np
import time

print("=" * 60)
print("MICROPHONE FIX - Testing Intel device specifically")
print("=" * 60)

# Use device 1 (Intel Smart Sound)
device = 1

print(f"\nUsing device {device}:")
print(sd.query_devices(device))
print()

print("Testing for 10 seconds - SPEAK LOUDLY:")
print()

def callback(indata, frames, time_info, status):
    volume = np.linalg.norm(indata) * 10
    bars = int(volume)
    if bars > 0:
        print('|' * min(bars, 60), f" {volume:.3f}")
    else:
        print(f" [silence] {volume:.3f}")

try:
    stream = sd.InputStream(
        callback=callback,
        channels=2,  # Intel mic has 4 channels, try 2
        samplerate=16000,
        device=device
    )

    with stream:
        time.sleep(10)

    print("\nTest complete!")

except Exception as e:
    print(f"\nERROR: {e}")
    print("\nTrying with 1 channel...")

    stream = sd.InputStream(
        callback=callback,
        channels=1,
        samplerate=16000,
        device=device
    )

    with stream:
        time.sleep(10)

    print("\nTest complete!")
