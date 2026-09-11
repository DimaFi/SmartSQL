# Architecture Decision Log

This file records significant architectural decisions. New decisions should be added without silently rewriting accepted decisions.

## D001 - C# / .NET 10 LTS

The project uses C# and targets .NET 10 LTS.

Status: Accepted

## D002 - SQLite as Default Local DBMS

SQLite is planned as the default embedded local DBMS.

Status: Accepted

## D003 - SQLite Through Provider

SQLite is not part of the Domain architecture and must be connected through a provider.

Status: Accepted

## D004 - Dapper as Preliminary SQL Access Choice

Dapper is the preliminary choice for SQL access in future infrastructure providers.

Status: Accepted

## D005 - Local-First Design

The system is designed as local-first.

Status: Accepted

## D006 - Data Portability

Data portability is a key requirement.

Status: Accepted

## D007 - No Direct Arbitrary SQL Execution by AI

AI must not directly execute arbitrary SQL.

Status: Accepted

## D008 - User Confirmation for AI Mutations

By default, mutating AI actions require user confirmation.

Status: Accepted

## D009 - Camp Domain as Validation Scenario

The main diploma validation domain is structured data management for children's camp participants, but the architecture remains universal.

Status: Accepted
