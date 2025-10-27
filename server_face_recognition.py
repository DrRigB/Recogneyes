"""
Face Recognition Server for Magic Leap
Receives face images from Magic Leap, processes with ArcFace, returns recognition result
"""

from flask import Flask, request, jsonify
import cv2
import numpy as np
import json
import os
from scipy import ndimage

app = Flask(__name__)

# Configuration
ARCFACE_MODEL_PATH = "Assets/StreamingAssets/arcface.onnx"
EMBEDDINGS_PATH = "Assets/StreamingAssets/face_embeddings.json"
SIMILARITY_THRESHOLD = 0.25  # Stricter threshold to avoid false positives (good matches are 0.35-0.46, bad are 0.15-0.16)
ANONYMOUS_NAMES = ["Obama", "Jshlatt", "ScarlettJohansson"]
MODEL_INPUT_SIZE = 112

# Global variables
arcface_model = None
known_embeddings = {}

def load_arcface_model():
    """Load ArcFace model using OpenCV DNN"""
    global arcface_model
    
    if not os.path.exists(ARCFACE_MODEL_PATH):
        print(f"[ERROR] Model not found: {ARCFACE_MODEL_PATH}")
        return False
    
    try:
        arcface_model = cv2.dnn.readNetFromONNX(ARCFACE_MODEL_PATH)
        print(f"[OK] ArcFace model loaded from {ARCFACE_MODEL_PATH}")
        return True
    except Exception as e:
        print(f"[ERROR] Failed to load ArcFace model: {e}")
        return False

def load_embeddings():
    """Load pre-computed embeddings from JSON"""
    global known_embeddings
    
    if not os.path.exists(EMBEDDINGS_PATH):
        print(f"[ERROR] Embeddings file not found: {EMBEDDINGS_PATH}")
        return False
    
    try:
        with open(EMBEDDINGS_PATH, 'r') as f:
            data = json.load(f)
        
        known_embeddings.clear()
        for person in data['embeddings']:
            name = person['name']
            embeddings = [np.array(emb['values'], dtype=np.float32) for emb in person['embeddings']]
            # Normalize embeddings
            embeddings = [emb / (np.linalg.norm(emb) + 1e-6) for emb in embeddings]
            known_embeddings[name] = embeddings
        
        print(f"[OK] Loaded embeddings for {len(known_embeddings)} people:")
        for name, embs in known_embeddings.items():
            print(f"   - {name}: {len(embs)} embeddings")
        return True
    except Exception as e:
        print(f"[ERROR] Failed to load embeddings: {e}")
        return False

def generate_arcface_embedding(face_image):
    """Generate ArcFace embedding from face image - SIMPLIFIED for consistency"""
    try:
        print(f"[DEBUG] Input face shape: {face_image.shape}, dtype: {face_image.dtype}")
        print(f"[DEBUG] Input face pixel range: {face_image.min():.3f} to {face_image.max():.3f}")
        
        # Resize to model input size
        resized = cv2.resize(face_image, (MODEL_INPUT_SIZE, MODEL_INPUT_SIZE), interpolation=cv2.INTER_LINEAR)
        print(f"[DEBUG] After resize: {resized.shape}, range: {resized.min():.3f} to {resized.max():.3f}")
        
        # Convert grayscale to BGR if needed
        if len(resized.shape) == 2:  # Grayscale
            resized = cv2.cvtColor(resized, cv2.COLOR_GRAY2BGR)
            print(f"[DEBUG] Converted grayscale to BGR: {resized.shape}")
        
        # DON'T convert BGR to RGB manually - let blobFromImage handle it with swapRB
        print(f"[DEBUG] Before blob (BGR): {resized.shape}, range: {resized.min():.3f} to {resized.max():.3f}")
        
        # Create blob with ArcFace normalization: [-1, 1] range
        # swapRB=True converts BGR to RGB automatically
        blob = cv2.dnn.blobFromImage(resized, 1.0/127.5, (MODEL_INPUT_SIZE, MODEL_INPUT_SIZE), 
                                     (127.5, 127.5, 127.5), swapRB=True, crop=False)
        print(f"[DEBUG] Blob shape: {blob.shape}, range: {blob.min():.6f} to {blob.max():.6f}")
        
        # Run inference
        arcface_model.setInput(blob)
        embedding = arcface_model.forward()
        print(f"[DEBUG] Raw embedding shape: {embedding.shape}, range: {embedding.min():.6f} to {embedding.max():.6f}")
        
        # Flatten and normalize
        embedding = embedding.flatten()
        embedding_norm = embedding / (np.linalg.norm(embedding) + 1e-6)
        print(f"[DEBUG] Final embedding norm: {np.linalg.norm(embedding_norm):.6f}, range: {embedding_norm.min():.6f} to {embedding_norm.max():.6f}")
        
        return embedding_norm
    except Exception as e:
        print(f"[ERROR] Error generating embedding: {e}")
        import traceback
        traceback.print_exc()
        return None

def cosine_similarity(emb1, emb2):
    """Calculate cosine similarity between two embeddings"""
    return np.dot(emb1, emb2) / (np.linalg.norm(emb1) * np.linalg.norm(emb2) + 1e-6)

def align_face_landmarks(face_image):
    """
    Align face using facial landmarks to handle different angles/positions
    Uses similarity transform to align eyes and ensure consistent orientation
    """
    try:
        # Detect face landmarks using dlib-style detector (cascade + correlation)
        # For now, use cv2 to detect eyes as a simple alignment
        
        gray = cv2.cvtColor(face_image, cv2.COLOR_BGR2GRAY) if len(face_image.shape) == 3 else face_image
        
        # Load eye cascade
        eye_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_eye.xml')
        eyes = eye_cascade.detectMultiScale(gray, scaleFactor=1.3, minNeighbors=4, minSize=(15, 15))
        
        # If we found 2 eyes, align based on them
        if len(eyes) >= 2:
            # Sort eyes by x coordinate (left to right)
            eyes = sorted(eyes, key=lambda e: e[0])[:2]
            
            # Get eye centers - convert to Python int to avoid numpy type issues
            left_eye = (int(eyes[0][0] + eyes[0][2]//2), int(eyes[0][1] + eyes[0][3]//2))
            right_eye = (int(eyes[1][0] + eyes[1][2]//2), int(eyes[1][1] + eyes[1][3]//2))
            
            # Calculate angle between eyes
            dy = right_eye[1] - left_eye[1]
            dx = right_eye[0] - left_eye[0]
            angle = np.degrees(np.arctan2(dy, dx))
            
            # Get center point (between eyes) - ensure it's Python int
            center = (int((left_eye[0] + right_eye[0]) / 2), int((left_eye[1] + right_eye[1]) / 2))
            
            # Rotate image to align eyes horizontally
            rotation_matrix = cv2.getRotationMatrix2D(center, angle, scale=1.0)
            aligned = cv2.warpAffine(face_image, rotation_matrix, (face_image.shape[1], face_image.shape[0]))
            
            return aligned
        
        # If eyes not found, return original (cascade isn't perfect)
        return face_image
        
    except Exception as e:
        print(f"[WARN] Face alignment failed: {e} - using original image")
        return face_image

def normalize_face_image(face_image, target_size=112):
    """
    Normalize face image for consistent recognition:
    - Resize to target size
    - Apply histogram equalization
    - Normalize lighting
    """
    try:
        # Convert to grayscale if needed
        if len(face_image.shape) == 3:
            gray = cv2.cvtColor(face_image, cv2.COLOR_BGR2GRAY)
        else:
            gray = face_image
        
        # Resize to target size
        resized = cv2.resize(gray, (target_size, target_size), interpolation=cv2.INTER_LINEAR)
        
        # Apply CLAHE (Contrast Limited Adaptive Histogram Equalization)
        # This normalizes lighting without over-equalizing
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        normalized = clahe.apply(resized)
        
        # Apply mild Gaussian blur to reduce noise (helps with phone quality variations)
        normalized = cv2.GaussianBlur(normalized, (3, 3), 0)
        
        return normalized
        
    except Exception as e:
        print(f"[ERROR] Face normalization failed: {e}")
        return face_image

def recognize_face(face_image):
    """Recognize face from image"""
    # Generate embedding for live face
    live_embedding = generate_arcface_embedding(face_image)
    if live_embedding is None:
        return "Error", 0.0
    
    # Find best match and track all person averages
    person_scores = {}  # Track average similarity per person
    
    for person_name, stored_embeddings in known_embeddings.items():
        similarities = []
        for stored_embedding in stored_embeddings:
            similarity = cosine_similarity(live_embedding, stored_embedding)
            similarities.append(similarity)
        
        # Calculate average similarity for this person
        avg_similarity = sum(similarities) / len(similarities)
        person_scores[person_name] = avg_similarity
    
    # Find best match based on AVERAGE similarity per person
    best_match = "Unknown"
    best_similarity = 0.0
    for person_name, avg_score in person_scores.items():
        if avg_score > best_similarity:
            best_similarity = avg_score
            best_match = person_name
    
    # DEBUG: Print detailed similarity analysis
    print(f"[DEBUG] Input embedding norm: {np.linalg.norm(live_embedding):.6f}")
    print(f"[DEBUG] Similarity scores:")
    for person, score in sorted(person_scores.items(), key=lambda x: x[1], reverse=True):
        embeddings_count = len(known_embeddings[person])
        print(f"  {person}: {score:.3f} (from {embeddings_count} embeddings)")
        # Show individual embedding similarities for first person
        if person == sorted(person_scores.items(), key=lambda x: x[1], reverse=True)[0][0]:
            similarities = []
            for stored_embedding in known_embeddings[person]:
                similarity = cosine_similarity(live_embedding, stored_embedding)
                similarities.append(similarity)
                print(f"    Individual similarities: {[f'{s:.6f}' for s in similarities[:3]]}{'...' if len(similarities) > 3 else ''}")
    
    # Apply threshold
    if best_similarity >= SIMILARITY_THRESHOLD:
        # Check if person should be shown as anonymous
        if best_match in ANONYMOUS_NAMES:
            print(f"[ANON] Recognized {best_match} (similarity: {best_similarity:.3f}) -> Showing as Unknown")
            return "Unknown", best_similarity
        else:
            print(f"[OK] Recognized: {best_match} (similarity: {best_similarity:.3f})")
            return best_match, best_similarity
    else:
        print(f"[REJECT] Best match {best_match} below threshold (similarity: {best_similarity:.3f} < {SIMILARITY_THRESHOLD})")
        return "Unknown", best_similarity

@app.route('/recognize', methods=['POST'])
def recognize():
    """API endpoint to recognize face from image"""
    try:
        # Get image data from request
        image_data = request.data
        
        if len(image_data) == 0:
            return jsonify({'error': 'No image data received'}), 400
        
        # Decode image
        nparr = np.frombuffer(image_data, np.uint8)
        image = cv2.imdecode(nparr, cv2.IMREAD_COLOR)
        
        if image is None:
            return jsonify({'error': 'Failed to decode image'}), 400
        
        print(f"[RECV] Received image: {image.shape}")
        
        # Recognize face directly - no preprocessing needed, ArcFace handles it
        name, confidence = recognize_face(image)
        
        # Return result
        return jsonify({
            'name': name,
            'confidence': float(confidence),
            'success': True
        })
        
    except Exception as e:
        print(f"[ERROR] Error processing request: {e}")
        return jsonify({'error': str(e)}), 500

@app.route('/health', methods=['GET'])
def health():
    """Health check endpoint"""
    return jsonify({
        'status': 'ok',
        'model_loaded': arcface_model is not None,
        'embeddings_loaded': len(known_embeddings) > 0,
        'num_people': len(known_embeddings)
    })

if __name__ == '__main__':
    print("=" * 60)
    print("Face Recognition Server for Magic Leap")
    print("=" * 60)
    
    # Load model and embeddings
    if not load_arcface_model():
        print("[ERROR] Failed to load ArcFace model. Exiting.")
        exit(1)
    
    if not load_embeddings():
        print("[ERROR] Failed to load embeddings. Exiting.")
        exit(1)
    
    print("\n[OK] Server ready!")
    print("[SERVER] Starting Flask server on http://0.0.0.0:5000")
    print("   Access from Magic Leap using your PC's IP address")
    print("   Example: http://10.200.57.186:5000/recognize")
    print("\n" + "=" * 60)
    
    # Start server
    app.run(host='0.0.0.0', port=5000, debug=False)

