# Evaluations

Evaluations ask the agent a set of questions and grade the answers, so you see whether a change to sources, prompts or models made answers better or worse. Run them on [Evaluations](/evals).

## Datasets

A dataset is a list of questions, each with an optional expected answer and facts it should contain.

- Create a dataset on [Evaluations](/evals) and add questions one by one or upload them.
- From a real conversation, use Add to dataset to keep a question your visitors asked ([Inbox](/inbox)).

## Run an evaluation

1. Open [Evaluations](/evals) and start a run.
2. Choose the dataset and the agent, or keep the settings of the last run.
3. Wait for the run to finish; it runs in the background.

## Read the results

Each run shows how well answers found the right sources, cited them and answered correctly, graded by the judge model. Open a run to see every question with its answer and grades, and compare it with the run before. A quality gate can fail a run that is worse than the baseline.

## Before you start

Evaluations need a chat model and a judge model; the judge uses the chat model unless you choose another on [AI models](/models).
