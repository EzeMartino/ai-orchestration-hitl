import json
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "python-agents" / "data_agent"))

import financial_analysis  # noqa: E402


def metric(name, period, value, confidence=0.9):
    return {
        "name": name,
        "period": period,
        "value": value,
        "unit": "USD millions",
        "currency": "USD",
        "source": "unit_test",
        "sourcePage": 1,
        "confidence": confidence,
    }


def call(function, payload):
    return json.loads(function(json.dumps(payload)))


class FinancialAnalysisRatioTests(unittest.TestCase):
    def test_computes_core_ratios(self):
        response = call(
            financial_analysis.compute_financial_ratios,
            {
                "metrics": sample_metrics(),
                "requestedRatios": [
                    "gross_margin",
                    "ebitda_margin",
                    "current_ratio",
                    "net_debt_to_ebitda",
                ],
            },
        )

        ratios = {(item["name"], item["period"]): item for item in response["ratios"]}

        self.assertAlmostEqual(ratios[("gross_margin", "2024A")]["value"], 0.45)
        self.assertAlmostEqual(ratios[("ebitda_margin", "2024A")]["value"], 0.25)
        self.assertAlmostEqual(ratios[("current_ratio", "2024A")]["value"], 2.0)
        self.assertAlmostEqual(ratios[("net_debt_to_ebitda", "2025E")]["value"], 3.75)
        self.assertEqual(ratios[("gross_margin", "2024A")]["source"], "computed")
        self.assertLessEqual(ratios[("gross_margin", "2024A")]["confidence"], 0.9)

    def test_skips_missing_ratio_inputs_and_returns_warning(self):
        response = call(
            financial_analysis.compute_financial_ratios,
            {
                "metrics": [metric("revenue", "2024A", 100)],
                "requestedRatios": ["gross_margin"],
            },
        )

        self.assertEqual(response["ratios"], [])
        self.assertTrue(any("Missing numerator input" in item for item in response["warnings"]))

    def test_handles_zero_denominator_safely(self):
        response = call(
            financial_analysis.compute_financial_ratios,
            {
                "metrics": [
                    metric("revenue", "2024A", 0),
                    metric("ebitda", "2024A", 25),
                ],
                "requestedRatios": ["ebitda_margin"],
            },
        )

        self.assertEqual(response["ratios"], [])
        self.assertTrue(any("Zero denominator" in item for item in response["warnings"]))

    def test_vista_fixture_can_feed_ratio_computation(self):
        fixture_path = (
            REPO_ROOT
            / "backend"
            / "Orchestration.Tests"
            / "Fixtures"
            / "vista_energy_sample_metrics.json"
        )
        fixture = json.loads(fixture_path.read_text(encoding="utf-8"))

        response = call(
            financial_analysis.compute_financial_ratios,
            {
                "metrics": fixture["metrics"],
                "requestedRatios": [
                    "gross_margin",
                    "ebitda_margin",
                    "net_debt_to_ebitda",
                ],
            },
        )

        self.assertGreaterEqual(len(response["ratios"]), 9)
        self.assertEqual(response["warnings"], [])


class FinancialAnalysisComparisonTests(unittest.TestCase):
    def test_computes_absolute_and_percentage_change(self):
        response = call(
            financial_analysis.compare_periods,
            {
                "metrics": sample_metrics(),
                "basePeriod": "2024A",
                "comparisonPeriod": "2025E",
                "metricsToCompare": ["total_debt"],
            },
        )

        comparison = response["comparisons"][0]

        self.assertEqual(comparison["absoluteChange"], 20.0)
        self.assertAlmostEqual(comparison["percentageChange"], 0.166667)
        self.assertEqual(comparison["severity"], "Medium")
        self.assertEqual(response["evidence"][0]["metricName"], "total_debt")

    def test_flags_high_revenue_drop(self):
        response = call(
            financial_analysis.compare_periods,
            {
                "metrics": sample_metrics(),
                "basePeriod": "2024A",
                "comparisonPeriod": "2025E",
                "metricsToCompare": ["revenue"],
            },
        )

        comparison = response["comparisons"][0]

        self.assertEqual(comparison["severity"], "High")
        self.assertLess(comparison["percentageChange"], -0.15)

    def test_handles_missing_base_period(self):
        response = call(
            financial_analysis.compare_periods,
            {
                "metrics": sample_metrics(),
                "basePeriod": "2023A",
                "comparisonPeriod": "2025E",
                "metricsToCompare": ["revenue"],
            },
        )

        self.assertEqual(response["comparisons"], [])
        self.assertTrue(any("Missing metric revenue" in item for item in response["warnings"]))


class FinancialAnalysisRiskSignalTests(unittest.TestCase):
    def test_flags_low_current_ratio_high_leverage_and_negative_fcf(self):
        ratios = call(
            financial_analysis.compute_financial_ratios,
            {
                "metrics": sample_metrics(),
                "requestedRatios": [
                    "current_ratio",
                    "quick_ratio",
                    "net_debt_to_ebitda",
                    "debt_to_equity",
                    "interest_coverage",
                ],
            },
        )["ratios"]

        response = call(
            financial_analysis.detect_financial_risk_signals,
            {"metrics": sample_metrics(), "ratios": ratios},
        )

        codes = {signal["code"] for signal in response["signals"]}

        self.assertIn("LOW_CURRENT_RATIO", codes)
        self.assertIn("HIGH_NET_DEBT_TO_EBITDA", codes)
        self.assertIn("NEGATIVE_FREE_CASH_FLOW", codes)

        low_current_ratio = next(
            signal for signal in response["signals"] if signal["code"] == "LOW_CURRENT_RATIO"
        )
        self.assertEqual(low_current_ratio["metric"], "current_ratio")
        self.assertEqual(low_current_ratio["period"], "2025E")
        self.assertAlmostEqual(low_current_ratio["value"], 0.777778)
        self.assertEqual(low_current_ratio["thresholdCode"], "LOW_CURRENT_RATIO")
        self.assertEqual(low_current_ratio["thresholdOperator"], "<")
        self.assertEqual(low_current_ratio["thresholdValue"], 1.0)
        self.assertIn("current_ratio", low_current_ratio["reason"])
        self.assertIn("< 1.0", low_current_ratio["reason"])

    def test_does_not_flag_healthy_values(self):
        ratios = call(
            financial_analysis.compute_financial_ratios,
            {
                "metrics": healthy_metrics(),
                "requestedRatios": [
                    "current_ratio",
                    "quick_ratio",
                    "net_debt_to_ebitda",
                    "debt_to_equity",
                    "interest_coverage",
                    "ebitda_margin",
                ],
            },
        )["ratios"]

        response = call(
            financial_analysis.detect_financial_risk_signals,
            {"metrics": healthy_metrics(), "ratios": ratios},
        )

        self.assertEqual(response["signals"], [])

    def test_respects_configured_threshold_metadata(self):
        ratios = [
            {
                "name": "net_debt_to_ebitda",
                "period": "2025E",
                "value": 0.625,
                "unit": "x",
            }
        ]

        response = call(
            financial_analysis.detect_financial_risk_signals,
            {
                "metrics": healthy_metrics(),
                "ratios": ratios,
                "thresholds": [
                    {
                        "code": "CUSTOM_HIGH_NET_DEBT_TO_EBITDA",
                        "metric": "net_debt_to_ebitda",
                        "operator": ">=",
                        "value": 0.5,
                        "severity": "High",
                        "description": "Custom leverage threshold.",
                    }
                ],
            },
        )

        signal = response["signals"][0]
        self.assertEqual(signal["code"], "CUSTOM_HIGH_NET_DEBT_TO_EBITDA")
        self.assertEqual(signal["metric"], "net_debt_to_ebitda")
        self.assertEqual(signal["thresholdCode"], "CUSTOM_HIGH_NET_DEBT_TO_EBITDA")
        self.assertEqual(signal["thresholdOperator"], ">=")
        self.assertEqual(signal["thresholdValue"], 0.5)
        self.assertIn(">= 0.5", signal["reason"])

    def test_missing_metric_inputs_do_not_crash_risk_detection(self):
        response = call(
            financial_analysis.detect_financial_risk_signals,
            {"metrics": [metric("revenue", "2024A", 100)], "ratios": []},
        )

        self.assertEqual(response["signals"], [])
        self.assertTrue(response["limitations"])


class FinancialAnalysisEvidenceSummaryTests(unittest.TestCase):
    def test_summarizes_highest_severity_first_and_respects_max_items(self):
        response = call(
            financial_analysis.summarize_quantitative_evidence,
            {
                "signals": [
                    {
                        "name": "LOW_CURRENT_RATIO",
                        "severity": "Medium",
                        "period": "2025E",
                        "summary": "Liquidity should be reviewed.",
                        "evidence": [
                            {
                                "metricName": "current_ratio",
                                "period": "2025E",
                                "value": 0.78,
                                "threshold": 1.0,
                                "unit": "x",
                                "severity": "Medium",
                                "interpretation": "Possible liquidity issue.",
                            }
                        ],
                    },
                    {
                        "name": "HIGH_NET_DEBT_TO_EBITDA",
                        "severity": "High",
                        "period": "2025E",
                        "summary": "Leverage requires review.",
                        "evidence": [
                            {
                                "metricName": "net_debt_to_ebitda",
                                "period": "2025E",
                                "value": 3.75,
                                "threshold": 3.0,
                                "unit": "x",
                                "severity": "High",
                                "interpretation": "Leverage threshold exceeded.",
                            }
                        ],
                    },
                ],
                "comparisons": [
                    {
                        "metricName": "revenue",
                        "comparisonPeriod": "2025E",
                        "comparisonValue": 80,
                        "unit": "USD millions",
                        "severity": "Low",
                        "explanation": "Revenue was reviewed.",
                    }
                ],
                "maxItems": 2,
                "limitations": ["Structured metrics only."],
            },
        )

        self.assertEqual(len(response["evidence"]), 2)
        self.assertEqual(response["evidence"][0]["severity"], "High")
        self.assertIn("Structured metrics only.", response["limitations"])
        self.assertIn("Human review recommended", response["summary"])

    def test_includes_limitation_when_no_evidence_exists(self):
        response = call(
            financial_analysis.summarize_quantitative_evidence,
            {"signals": [], "comparisons": [], "maxItems": 3},
        )

        self.assertEqual(response["evidence"], [])
        self.assertTrue(response["limitations"])


def sample_metrics():
    return [
        metric("revenue", "2024A", 100),
        metric("gross_profit", "2024A", 45),
        metric("ebitda", "2024A", 25, confidence=0.8),
        metric("ebit", "2024A", 18),
        metric("net_income", "2024A", 10),
        metric("current_assets", "2024A", 80),
        metric("current_liabilities", "2024A", 40),
        metric("cash", "2024A", 30),
        metric("total_debt", "2024A", 120),
        metric("net_debt", "2024A", 75),
        metric("equity", "2024A", 60),
        metric("free_cash_flow", "2024A", 12),
        metric("capex", "2024A", 20),
        metric("interest_expense", "2024A", 6),
        metric("revenue", "2025E", 80),
        metric("gross_profit", "2025E", 30),
        metric("ebitda", "2025E", 12),
        metric("ebit", "2025E", 4),
        metric("net_income", "2025E", 1),
        metric("current_assets", "2025E", 35),
        metric("current_liabilities", "2025E", 45),
        metric("cash", "2025E", 10),
        metric("total_debt", "2025E", 140),
        metric("net_debt", "2025E", 45),
        metric("equity", "2025E", 50),
        metric("free_cash_flow", "2025E", -5),
        metric("capex", "2025E", 40),
        metric("interest_expense", "2025E", 3),
    ]


def healthy_metrics():
    return [
        metric("revenue", "2024A", 100),
        metric("ebitda", "2024A", 35),
        metric("ebit", "2024A", 28),
        metric("current_assets", "2024A", 120),
        metric("current_liabilities", "2024A", 60),
        metric("cash", "2024A", 55),
        metric("total_debt", "2024A", 60),
        metric("net_debt", "2024A", 30),
        metric("equity", "2024A", 150),
        metric("free_cash_flow", "2024A", 20),
        metric("interest_expense", "2024A", 8),
        metric("revenue", "2025A", 112),
        metric("ebitda", "2025A", 40),
        metric("ebit", "2025A", 32),
        metric("current_assets", "2025A", 140),
        metric("current_liabilities", "2025A", 65),
        metric("cash", "2025A", 60),
        metric("total_debt", "2025A", 58),
        metric("net_debt", "2025A", 25),
        metric("equity", "2025A", 165),
        metric("free_cash_flow", "2025A", 24),
        metric("interest_expense", "2025A", 8),
    ]


if __name__ == "__main__":
    unittest.main()
