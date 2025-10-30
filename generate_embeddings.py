"""
Face Embedding Generator
Scans training images, generates ArcFace embeddings, and saves them to a JSON file
for the face recognition server.
"""

import os
import json
import cv2
import numpy as np

# --- Configuration (should match server_face_recognition.py) ---
ARCFACE_MODEL_PATH = "Assets/StreamingAssets/arcface.onnx"
EMBEDDINGS_PATH = "Assets/StreamingAssets/face_embeddings.json"
TRAINING_DATA_FOLDER = "Assets/StreamingAssets/Faces"
MODEL_INPUT_SIZE = 112
# Folders to ignore during training
IGNORED_FOLDERS = ["Unknown"] 

# Global model variable
arcface_model = None

def load_arcface_model():
    """Load ArcFace model using OpenCV DNN"""
    global arcface_model
    
    if not os.path.exists(ARCFACE_MODEL_PATH):
        print(f"[ERROR] Model not found: {ARCFACE_MODEL_PATH}")
        print("Please ensure 'arcface.onnx' is in the 'Assets/StreamingAssets/' folder.")
        return False
    
    try:
        arcface_model = cv2.dnn.readNetFromONNX(ARCFACE_MODEL_PATH)
        print(f"[OK] ArcFace model loaded from {ARCFACE_MODEL_PATH}")
        return True
    except Exception as e:
        print(f"[ERROR] Failed to load ArcFace model: {e}")
        return False

def generate_arcface_embedding(face_image):
    """
    Generate ArcFace embedding from a face image.
    This function is a copy from the server to ensure results are identical.
    """
    try:
        # Resize to model input size
        resized = cv2.resize(face_image, (MODEL_INPUT_SIZE, MODEL_INPUT_SIZE), interpolation=cv2.INTER_LINEAR)
        
        # Convert grayscale to BGR if needed
        if len(resized.shape) == 2:
            resized = cv2.cvtColor(resized, cv2.COLOR_GRAY2BGR)
        
        # Create blob with ArcFace normalization and BGR->RGB conversion
        blob = cv2.dnn.blobFromImage(resized, 1.0/127.5, (MODEL_INPUT_SIZE, MODEL_INPUT_SIZE), 
                                     (127.5, 127.5, 127.5), swapRB=True, crop=False)
        
        # Run inference
        arcface_model.setInput(blob)
        embedding = arcface_model.forward()
        
        # Flatten and normalize to get the final embedding vector
        embedding = embedding.flatten()
        embedding_norm = embedding / (np.linalg.norm(embedding) + 1e-6)
        
        return embedding_norm
    except Exception as e:
        print(f"[ERROR] Error generating embedding: {e}")
        return None

def main():
    """Main function to generate and save embeddings."""
    print("=" * 60)
    print("Recogneyes - Face Embedding Generator")
    print("=" * 60)
    
    if not load_arcface_model():
        return

    if not os.path.exists(TRAINING_DATA_FOLDER):
        print(f"[ERROR] Training data folder not found: {TRAINING_DATA_FOLDER}")
        return

    # Get list of person directories
    try:
        person_names = [d for d in os.listdir(TRAINING_DATA_FOLDER) if os.path.isdir(os.path.join(TRAINING_DATA_FOLDER, d))]
    except FileNotFoundError:
        print(f"[ERROR] Could not list directories in {TRAINING_DATA_FOLDER}. Does it exist?")
        return

    all_embeddings_data = {"embeddings": []}

    print(f"\nFound {len(person_names)} potential people. Starting processing...\n")

    for person_name in person_names:
        if person_name in IGNORED_FOLDERS:
            print(f"--- Skipping ignored folder: '{person_name}' ---")
            continue

        person_dir = os.path.join(TRAINING_DATA_FOLDER, person_name)
        
        try:
            image_files = [f for f in os.listdir(person_dir) if f.lower().endswith(('.png', '.jpg', '.jpeg'))]
        except FileNotFoundError:
            continue

        if not image_files:
            print(f"--- No images found for {person_name}, skipping. ---")
            continue
            
        print(f"--- Processing {person_name} ({len(image_files)} images) ---")
        
        person_embedding_list = []
        for image_file in image_files:
            image_path = os.path.join(person_dir, image_file)
            
            image = cv2.imread(image_path)
            if image is None:
                print(f"  - [WARN] Could not read {image_file}, skipping.")
                continue

            embedding = generate_arcface_embedding(image)
            if embedding is not None:
                person_embedding_list.append({"values": embedding.tolist()})
                print(f"  - [OK] Generated embedding for {image_file}")
            else:
                print(f"  - [FAIL] Failed to generate embedding for {image_file}")

        if person_embedding_list:
            all_embeddings_data["embeddings"].append({
                "name": person_name,
                "embeddings": person_embedding_list
            })

    # Write the final data to the JSON file
    if not all_embeddings_data["embeddings"]:
        print("\n[ERROR] No embeddings were generated. Please check your training image folders.")
        return

    try:
        with open(EMBEDDINGS_PATH, 'w') as f:
            json.dump(all_embeddings_data, f, indent=4)
        print(f"\n[SUCCESS] Saved embeddings for {len(all_embeddings_data['embeddings'])} people to {EMBEDDINGS_PATH}")
    except IOError as e:
        print(f"\n[ERROR] Failed to write to {EMBEDDINGS_PATH}: {e}")

if __name__ == "__main__":
    main()
