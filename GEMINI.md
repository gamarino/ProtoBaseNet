# Gemini AI Model Instructions

This document provides context and guidelines for interacting with the ProtoBaseNet project.

## Project Overview

ProtoBaseNet is an immutable object database written in C#. It is designed with a functional approach, where operations on data structures return new instances rather than modifying existing ones.

## Core Principles

- **Immutability**: All database collections and objects are immutable. Methods that appear to modify an object, such as `SetAt`, will return a new, modified instance, leaving the original unchanged.
- **Data Structures**: Collections are implemented using persistent data structures, inspired by AVL trees, to ensure efficient operations (insertions, deletions, lookups) while preserving immutability. This foundational structure should not be changed.
- **Serialization**: When objects are persisted to storage, only public attributes (those whose names do not start with an underscore `_`) are written. Upon loading, it is the responsibility of each class's `_load` method to reconstruct any internal (private) state if necessary. Pay close attention to attribute naming to ensure correct persistence.

## Code Style

- **Comments**: All public-facing classes, methods, and properties are documented using standard C# XML documentation comments. Comments should be written in professional English and explain the *what* and *why* of the code.
- **Naming Conventions**: Follow standard C# naming conventions (e.g., PascalCase for classes and public members, camelCase for local variables).