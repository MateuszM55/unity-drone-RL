# Quadcopter Navigation with Curriculum Learning

This repository contains the simulation environment and agent configuration for my Bachelor's thesis. The project is built in **Unity** utilizing the **ML-Agents** toolkit.

## Project Overview
The goal of this project is to develop a virtual 3D environment and a quadcopter model to study **Reinforcement Learning (RL)** efficiency. Specifically, it focuses on evaluating how **Curriculum Learning** strategies impact the training speed and stability of an agent performing navigation tasks.

## Key Features
* **Physics-based Quadcopter:** A custom drone model with force-based movement.
* **3D Training Environment:** A scalable environment designed for obstacle avoidance and pathfinding.
* **Curriculum Learning Implementation:** Staged training difficulty levels configured via ML-Agents `config` files to improve convergence.
* **Benchmarking:** Tools/Scenes setup to compare standard PPO training vs. Curriculum-based training.

## Tech Stack
* Unity 6000.3.1f1 LTS
* Unity ML-Agents Release 21
* Python (PyTorch)