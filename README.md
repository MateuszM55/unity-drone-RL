# Quadcopter Navigation with Curriculum Learning

This repository contains the simulation environment and agent configuration for my Bachelor's thesis. The project is built in **Unity** utilizing the **ML-Agents** toolkit.

## Project Overview
The goal of this project is to develop a virtual 3D environment and a quadcopter model to study **Reinforcement Learning (RL)** efficiency. Specifically, it focuses on evaluating how **Curriculum Learning** strategies impact the training speed and stability of an agent performing navigation tasks.

## Key Features

###  Advanced Quadcopter Simulation
* **Realistic Physics-Based Flight:** Custom drone controller with 4-rotor force-based propulsion system
* **Configurable Flight Parameters:** Adjustable thrust, damping, and aerodynamic properties

###  Intelligent Training System
* **Dynamic Obstacle Generation:** Procedurally configured environments with adjustable complexity
* **Multi-Arena Management:** Parallel training across multiple independent arenas for accelerated data collection

###  Custom Sensor Suite
* **Six-Axis Proximity Sensor:** SphereCast-based distance detection (Up, Down, Left, Right, Forward, Back)
* **Frontal Cone Proximity Sensor:** Fully custimizable set of 9 sensors with adjustable pointing angles and ray spread
* **Layer-Aware Detection:** One-hot encoding of detectable object types for semantic environment understanding
* **Volumetric Raycasting:** Sphere-radius collision detection prevents thin obstacles from being missed
* **Configurable Ray Parameters:** Adjustable detection range, sphere radius, and layer masks per sensor

###  Deep Reinforcement Learning
* **PPO-Based Training:** Proximal Policy Optimization with continuous action space (4 motor thrust outputs)
* **Rich Observation Space:** Combined positional, velocity, orientation, and sensor data streams
* **Reward Engineering:** Fine-tuned reward structure balancing task completion and efficiency

###  Development Features
* **Visual Debugging:** Gizmo-based visualization of sensor rays, hit detection, and agent state
* **Extensive Documentation:** Well-commented codebase with XML documentation

## Tech Stack
* Unity 6000.3.1f1 LTS
* Unity ML-Agents Release 21
* Python (PyTorch)