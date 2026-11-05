import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { agentsRoutes, builtInAgent } from "../../test/agentsApi";
import { evalDataset, evalDatasetList } from "../../test/evalsApi";
import { WIDGET_CONVERSATION_ID } from "../../test/inboxApi";
import { QUESTION_LABEL_ID } from "../../test/labelsApi";
import {
  jsonResponse,
  mockFetch,
  renderWithProviders,
  requestUrls,
} from "../../test/utils";
import { widgetDto, widgetGuid, widgetsRoutes } from "../../test/widgetsApi";
import { AnalyticsPage } from "./AnalyticsPage";

afterEach(() => {
  vi.unstubAllGlobals();
});

const DATASET_ID = "0199c1e2-0000-7000-8000-0000000b0001";

function analytics(days = 7) {
  const daily = Array.from({ length: days }, (_, index) => ({
    date: `2026-11-${String(index + 1).padStart(2, "0")}`,
    conversations: index === days - 1 ? 4 : 1,
    handedToPerson: index === days - 1 ? 1 : 0,
    thumbsUp: 1,
    thumbsDown: index === days - 1 ? 1 : 0,
    ratings: 0,
    averageRating: null,
  }));
  return {
    days,
    from: "2026-11-01T00:00:00Z",
    to: "2026-11-07T12:00:00Z",
    totals: {
      conversations: 10,
      answeredWithoutPerson: 8,
      handedToPerson: 2,
      ratings: 2,
      averageRating: 3.5,
      thumbsUp: 6,
      thumbsDown: 2,
      noSourceAnswers: 1,
    },
    daily,
    widgets: [
      {
        widgetId: widgetGuid(1),
        conversations: 7,
        handedToPerson: 2,
        thumbsUp: 4,
        thumbsDown: 2,
        ratings: 2,
        averageRating: 3.5,
      },
      {
        widgetId: null,
        conversations: 3,
        handedToPerson: 0,
        thumbsUp: 2,
        thumbsDown: 0,
        ratings: 0,
        averageRating: null,
      },
    ],
    unanswered: [
      {
        question: "Do you ship to Norway?",
        count: 3,
        reasons: ["handover", "no_sources"],
        lastAskedAt: "2026-11-07T10:00:00Z",
        conversationId: WIDGET_CONVERSATION_ID,
        widgetId: widgetGuid(1),
        messageId: "m-9",
        answer: "I could not find that.",
        comment: null,
      },
    ],
    labels: [
      {
        labelId: QUESTION_LABEL_ID,
        name: "Question",
        color: "blue",
        conversations: 6,
        previousConversations: 4,
      },
      {
        labelId: "refunds",
        name: "Refunds",
        color: "purple",
        conversations: 2,
        previousConversations: 0,
      },
    ],
    segments: [
      {
        segmentId: "0199c1e2-0000-7000-8000-0000000005e9",
        name: "Pro customers",
        color: "teal",
        contacts: 12,
        conversations: 20,
        handedToPerson: 5,
        handoverRate: 0.25,
        ratings: 4,
        averageRating: 4.5,
      },
    ],
  };
}

describe("AnalyticsPage", () => {
  it("shows the key numbers, the charts with their table and the questions the agent could not answer", async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      "GET /api/analytics": (url: URL) =>
        jsonResponse(analytics(Number(url.searchParams.get("days")))),
      ...widgetsRoutes(() => [widgetDto()]),
      ...agentsRoutes(() => [builtInAgent]),
    });
    renderWithProviders(<AnalyticsPage />, {
      route: "/analytics?days=7",
      path: "/analytics",
    });

    const numbers = await screen.findByRole("list", { name: "Key numbers" });
    expect(within(numbers).getByText("80%")).toBeInTheDocument();
    expect(within(numbers).getByText("75%")).toBeInTheDocument();
    expect(within(numbers).getByText("3.5/5")).toBeInTheDocument();

    const questions = screen.getByRole("list", {
      name: "Questions the agent could not answer",
    });
    expect(
      within(questions).getByText("Do you ship to Norway?"),
    ).toBeInTheDocument();
    expect(
      within(questions).getByText("Asked for a person"),
    ).toBeInTheDocument();
    expect(within(questions).getByText("No sources found")).toBeInTheDocument();
    expect(within(questions).getByText(/Asked 3 times/)).toBeInTheDocument();

    expect(
      screen.getByRole("table", { name: "Conversations per day" }),
    ).toBeInTheDocument();
    const perWidget = screen.getByRole("table", { name: "Numbers per widget" });
    expect(await within(perWidget).findByText("Shop help")).toBeInTheDocument();
    expect(within(perWidget).getByText("Portal chat")).toBeInTheDocument();

    const labels = screen.getByRole("list", {
      name: "Conversations per label",
    });
    const question = within(labels).getByRole("link", { name: /Question/ });
    expect(question).toHaveAttribute(
      "href",
      `/inbox?label=${QUESTION_LABEL_ID}&period=7`,
    );
    expect(
      within(question).getByText("+2 on the period before"),
    ).toBeInTheDocument();
    expect(within(labels).getByText("New")).toBeInTheDocument();

    const perSegment = screen.getByRole("table", { name: "Numbers per segment" });
    expect(within(perSegment).getByRole("link", { name: "Pro customers" })).toHaveAttribute(
      "href",
      "/contacts?segment=0199c1e2-0000-7000-8000-0000000005e9",
    );
    expect(within(perSegment).getByText("25%")).toBeInTheDocument();
    expect(within(perSegment).getByText("4.5")).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "90 days" }));
    await waitFor(() =>
      expect(
        requestUrls(fetchMock, "GET /api/analytics").map((url) =>
          url.searchParams.get("days"),
        ),
      ).toContain("90"),
    );
  });

  it("adds an unanswered question to a dataset, made on the spot", async () => {
    const user = userEvent.setup();
    const fetchMock = mockFetch({
      "GET /api/analytics": analytics(),
      "GET /api/evals/datasets": evalDatasetList({ datasets: [evalDataset()] }),
      "POST /api/evals/datasets": evalDataset({
        id: DATASET_ID,
        name: "Shipping",
        source: "custom",
        itemCount: 0,
        corpusDocuments: 0,
        tags: [],
      }),
      [`POST /api/evals/datasets/${DATASET_ID}/items`]: (
        _url: URL,
        init: RequestInit,
      ) => jsonResponse({ id: "i1", ...JSON.parse(String(init.body)) }, 201),
      ...widgetsRoutes(() => [widgetDto()]),
      ...agentsRoutes(() => [builtInAgent]),
    });
    renderWithProviders(<AnalyticsPage />, {
      route: "/analytics",
      path: "/analytics",
    });

    await user.click(
      await screen.findByRole("button", { name: "Add to dataset" }),
    );
    const dialog = await screen.findByRole("dialog", {
      name: "Add to a dataset",
    });
    expect(
      await within(dialog).findByRole("combobox", { name: "Dataset" }),
    ).toHaveTextContent("New dataset…");
    await user.type(
      within(dialog).getByRole("textbox", { name: "Name of the new dataset" }),
      "Shipping",
    );
    const answer = within(dialog).getByRole("textbox", {
      name: /Expected answer/,
    });
    await user.clear(answer);
    await user.type(answer, "Yes, in 5 days.");
    await user.type(
      within(dialog).getByRole("textbox", { name: /Facts/ }),
      "5 days",
    );
    await user.click(
      within(dialog).getByRole("button", { name: "Add question" }),
    );

    await waitFor(() =>
      expect(
        screen.queryByRole("dialog", { name: "Add to a dataset" }),
      ).toBeNull(),
    );
    const [created] = fetchMock.mock.calls.filter(
      ([url, init]) =>
        String(url).endsWith("/api/evals/datasets") && init?.method === "POST",
    );
    expect(JSON.parse(String(created![1]!.body))).toMatchObject({
      name: "Shipping",
    });
    const [added] = fetchMock.mock.calls.filter(([url]) =>
      String(url).endsWith(`/api/evals/datasets/${DATASET_ID}/items`),
    );
    expect(JSON.parse(String(added![1]!.body))).toEqual({
      question: "Do you ship to Norway?",
      referenceAnswer: "Yes, in 5 days.",
      expectedFacts: ["5 days"],
      expectedSources: [],
      answerable: true,
      tags: [],
      conversationId: WIDGET_CONVERSATION_ID,
      messageId: "m-9",
    });
  });
});
