import json
from typing import Any


DEFAULT_CONFIDENCE = 0.75
SEVERITY_ORDER = {
    "High": 3,
    "Medium": 2,
    "Low": 1,
    "Info": 0,
}


def compute_financial_ratios(request_json: str) -> str:
    request = _loads(request_json)
    metrics = request.get("metrics", [])
    requested_ratios = request.get("requestedRatios") or request.get("requested_ratios")
    warnings: list[str] = []
    limitations: list[str] = []
    ratios: list[dict[str, Any]] = []
    evidence: list[dict[str, Any]] = []

    by_period = _group_metrics(metrics)
    ratio_specs = _ratio_specs()
    ratio_names = requested_ratios or list(ratio_specs.keys())

    for period, period_metrics in by_period.items():
        for ratio_name in ratio_names:
            spec = ratio_specs.get(ratio_name)
            if spec is None:
                warnings.append(f"Unsupported ratio requested: {ratio_name}.")
                continue

            ratio = _compute_ratio(period, period_metrics, ratio_name, spec, warnings)
            if ratio is None:
                continue

            ratios.append(ratio)
            evidence.append(
                _evidence_item(
                    metric_name=ratio["name"],
                    period=period,
                    value=ratio["value"],
                    threshold=None,
                    unit=ratio["unit"],
                    severity="Info",
                    interpretation=ratio["interpretation"],
                )
            )

    if not ratios:
        limitations.append("No ratios were computed from the provided structured metrics.")

    return _dumps(
        {
            "ratios": ratios,
            "evidence": evidence,
            "warnings": _distinct(warnings),
            "limitations": _distinct(limitations),
        }
    )


def compare_periods(request_json: str) -> str:
    request = _loads(request_json)
    metrics = request.get("metrics", [])
    base_period = request.get("basePeriod") or request.get("fromPeriod")
    comparison_period = request.get("comparisonPeriod") or request.get("toPeriod")
    metrics_to_compare = request.get("metricsToCompare") or request.get("metricNames") or []
    warnings: list[str] = []
    limitations: list[str] = []
    comparisons: list[dict[str, Any]] = []
    evidence: list[dict[str, Any]] = []

    if not base_period or not comparison_period:
        return _dumps(
            {
                "comparisons": [],
                "warnings": ["basePeriod and comparisonPeriod are required."],
                "limitations": ["Period comparison could not run without both periods."],
            }
        )

    metric_index = _metric_index(metrics)
    if not metrics_to_compare:
        metrics_to_compare = sorted(
            {
                name
                for name, period in metric_index
                if period in {base_period, comparison_period}
            }
        )

    for metric_name in metrics_to_compare:
        base_metric = metric_index.get((metric_name, base_period))
        comparison_metric = metric_index.get((metric_name, comparison_period))

        if base_metric is None or comparison_metric is None:
            warnings.append(
                f"Missing metric {metric_name} for {base_period} or {comparison_period}."
            )
            continue

        base_value = _number(base_metric.get("value"))
        comparison_value = _number(comparison_metric.get("value"))
        if base_value is None or comparison_value is None:
            warnings.append(
                f"Metric {metric_name} has a non-numeric value for comparison."
            )
            continue

        absolute_change = comparison_value - base_value
        percentage_change = None if base_value == 0 else absolute_change / abs(base_value)
        severity = _comparison_severity(
            metric_name,
            base_value,
            comparison_value,
            percentage_change,
        )
        comparisons.append(
            {
                "metricName": metric_name,
                "basePeriod": base_period,
                "comparisonPeriod": comparison_period,
                "baseValue": _round(base_value),
                "comparisonValue": _round(comparison_value),
                "absoluteChange": _round(absolute_change),
                "percentageChange": _round(percentage_change),
                "severity": severity,
                "unit": comparison_metric.get("unit") or base_metric.get("unit") or "",
                "explanation": _comparison_explanation(
                    metric_name,
                    base_period,
                    comparison_period,
                    absolute_change,
                    percentage_change,
                    severity,
                ),
                "confidence": _min_confidence([base_metric, comparison_metric]),
            }
        )
        evidence.append(
            _evidence_item(
                metric_name=metric_name,
                period=comparison_period,
                value=comparison_value,
                threshold=base_value,
                unit=comparison_metric.get("unit") or base_metric.get("unit") or "",
                severity=severity,
                interpretation=_comparison_explanation(
                    metric_name,
                    base_period,
                    comparison_period,
                    absolute_change,
                    percentage_change,
                    severity,
                ),
            )
        )

    if not comparisons:
        limitations.append("No period comparisons were produced from the provided metrics.")

    return _dumps(
        {
            "comparisons": comparisons,
            "evidence": evidence,
            "warnings": _distinct(warnings),
            "limitations": _distinct(limitations),
        }
    )


def detect_financial_risk_signals(request_json: str) -> str:
    request = _loads(request_json)
    metrics = request.get("metrics", [])
    ratios = request.get("ratios", [])
    threshold_profile = request.get("thresholdProfile") or {}
    warnings: list[str] = []
    limitations: list[str] = []
    signals: list[dict[str, Any]] = []
    evidence: list[dict[str, Any]] = []

    ratio_index = _ratio_index(ratios)
    metric_index = _metric_index(metrics)
    periods = sorted({period for _, period in metric_index} | {period for _, period in ratio_index})

    _add_ratio_threshold_signal(
        signals,
        evidence,
        ratio_index,
        "current_ratio",
        threshold_profile.get("currentRatioMin", 1.0),
        "LOW_CURRENT_RATIO",
        "Current ratio is below 1.0. Human review recommended.",
        "Medium",
    )
    _add_ratio_threshold_signal(
        signals,
        evidence,
        ratio_index,
        "quick_ratio",
        threshold_profile.get("quickRatioMin", 0.8),
        "LOW_QUICK_RATIO",
        "Quick ratio is below 0.8. Liquidity should be reviewed.",
        "Medium",
    )
    _add_ratio_threshold_signal(
        signals,
        evidence,
        ratio_index,
        "net_debt_to_ebitda",
        threshold_profile.get("netDebtToEbitdaMax", 3.0),
        "HIGH_NET_DEBT_TO_EBITDA",
        "Net debt to EBITDA is above the configured threshold.",
        "High",
        greater_than=True,
    )
    _add_ratio_threshold_signal(
        signals,
        evidence,
        ratio_index,
        "debt_to_equity",
        threshold_profile.get("debtToEquityMax", 2.0),
        "HIGH_DEBT_TO_EQUITY",
        "Debt to equity is above the configured threshold.",
        "High",
        greater_than=True,
    )
    _add_ratio_threshold_signal(
        signals,
        evidence,
        ratio_index,
        "interest_coverage",
        threshold_profile.get("interestCoverageMin", 2.0),
        "LOW_INTEREST_COVERAGE",
        "Interest coverage is below the configured threshold.",
        "High",
    )

    for (metric_name, period), metric in metric_index.items():
        value = _number(metric.get("value"))
        if value is None:
            continue
        if metric_name == "free_cash_flow" and value < 0:
            _append_signal(
                signals,
                evidence,
                "NEGATIVE_FREE_CASH_FLOW",
                "High",
                period,
                "Free cash flow is negative. Human review recommended.",
                [
                    _evidence_item(
                        "free_cash_flow",
                        period,
                        value,
                        0,
                        metric.get("unit") or "",
                        "High",
                        "Free cash flow turned negative.",
                    )
                ],
            )

    if len(periods) < 2:
        limitations.append("Not enough comparable periods to detect trend-based risk signals.")
    else:
        _detect_margin_compression(signals, evidence, ratio_index, limitations)
        _detect_revenue_drop(signals, evidence, metric_index)
        _detect_capex_spike(signals, evidence, metric_index)
        _detect_forecast_dependency(signals, evidence, metric_index)

    return _dumps(
        {
            "signals": signals,
            "evidence": evidence,
            "warnings": _distinct(warnings),
            "limitations": _distinct(limitations),
        }
    )


def summarize_quantitative_evidence(request_json: str) -> str:
    request = _loads(request_json)
    signals = request.get("signals", [])
    comparisons = request.get("comparisons", [])
    max_items = int(request.get("maxItems") or 5)
    warnings = list(request.get("warnings") or [])
    limitations = list(request.get("limitations") or [])
    evidence: list[dict[str, Any]] = []

    for signal in signals:
        for item in signal.get("evidence", []):
            enriched = dict(item)
            enriched.setdefault("severity", signal.get("severity", "Info"))
            enriched.setdefault("signalName", signal.get("name"))
            evidence.append(enriched)

    for comparison in comparisons:
        evidence.append(
            _evidence_item(
                metric_name=comparison.get("metricName", "unknown"),
                period=comparison.get("comparisonPeriod", ""),
                value=comparison.get("comparisonValue"),
                threshold=None,
                unit=comparison.get("unit", ""),
                severity=comparison.get("severity", "Info"),
                interpretation=comparison.get("explanation", ""),
            )
        )

    evidence = sorted(
        evidence,
        key=lambda item: SEVERITY_ORDER.get(item.get("severity", "Info"), 0),
        reverse=True,
    )[:max_items]

    if not signals and not comparisons:
        limitations.append("No quantitative signals or comparisons were provided.")

    summary = _summary_text(signals, evidence)

    return _dumps(
        {
            "summary": summary,
            "evidence": evidence,
            "warnings": _distinct(warnings),
            "limitations": _distinct(limitations),
        }
    )


def _loads(request_json: str) -> dict[str, Any]:
    try:
        value = json.loads(request_json or "{}")
    except json.JSONDecodeError:
        return {}

    return value if isinstance(value, dict) else {}


def _dumps(value: dict[str, Any]) -> str:
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def _metric_index(metrics: list[dict[str, Any]]) -> dict[tuple[str, str], dict[str, Any]]:
    index: dict[tuple[str, str], dict[str, Any]] = {}
    for metric in metrics:
        name = metric.get("name")
        period = metric.get("period")
        if name and period:
            index[(str(name), str(period))] = metric
    return index


def _ratio_index(ratios: list[dict[str, Any]]) -> dict[tuple[str, str], dict[str, Any]]:
    index: dict[tuple[str, str], dict[str, Any]] = {}
    for ratio in ratios:
        name = ratio.get("name")
        period = ratio.get("period")
        if name and period:
            index[(str(name), str(period))] = ratio
    return index


def _group_metrics(metrics: list[dict[str, Any]]) -> dict[str, dict[str, dict[str, Any]]]:
    grouped: dict[str, dict[str, dict[str, Any]]] = {}
    for metric in metrics:
        name = metric.get("name")
        period = metric.get("period")
        if not name or not period:
            continue
        grouped.setdefault(str(period), {})[str(name)] = metric
    return grouped


def _ratio_specs() -> dict[str, dict[str, Any]]:
    return {
        "gross_margin": {
            "numerator": ["gross_profit"],
            "denominator": ["revenue"],
            "formula": "gross_profit / revenue",
            "unit": "ratio",
        },
        "operating_margin": {
            "numerator": ["operating_income", "ebit"],
            "denominator": ["revenue"],
            "formula": "operating_income / revenue",
            "unit": "ratio",
        },
        "ebitda_margin": {
            "numerator": ["ebitda"],
            "denominator": ["revenue"],
            "formula": "ebitda / revenue",
            "unit": "ratio",
        },
        "net_margin": {
            "numerator": ["net_income"],
            "denominator": ["revenue"],
            "formula": "net_income / revenue",
            "unit": "ratio",
        },
        "current_ratio": {
            "numerator": ["current_assets"],
            "denominator": ["current_liabilities"],
            "formula": "current_assets / current_liabilities",
            "unit": "x",
        },
        "quick_ratio": {
            "sum_numerator": ["cash", "short_term_investments", "receivables"],
            "denominator": ["current_liabilities"],
            "formula": "(cash + short_term_investments + receivables) / current_liabilities",
            "unit": "x",
        },
        "debt_to_equity": {
            "numerator": ["total_debt"],
            "denominator": ["equity"],
            "formula": "total_debt / equity",
            "unit": "x",
        },
        "net_debt_to_ebitda": {
            "numerator": ["net_debt"],
            "denominator": ["ebitda"],
            "formula": "net_debt / ebitda",
            "unit": "x",
        },
        "interest_coverage": {
            "numerator": ["ebit"],
            "denominator": ["interest_expense"],
            "formula": "ebit / interest_expense",
            "unit": "x",
        },
        "fcf_margin": {
            "numerator": ["free_cash_flow"],
            "denominator": ["revenue"],
            "formula": "free_cash_flow / revenue",
            "unit": "ratio",
        },
        "capex_to_revenue": {
            "numerator": ["capex"],
            "denominator": ["revenue"],
            "formula": "capex / revenue",
            "unit": "ratio",
        },
    }


def _compute_ratio(
    period: str,
    metrics: dict[str, dict[str, Any]],
    ratio_name: str,
    spec: dict[str, Any],
    warnings: list[str],
) -> dict[str, Any] | None:
    inputs: list[dict[str, Any]] = []

    if "sum_numerator" in spec:
        numerator = 0.0
        found_any = False
        for metric_name in spec["sum_numerator"]:
            metric = metrics.get(metric_name)
            if metric is None:
                continue
            value = _number(metric.get("value"))
            if value is None:
                continue
            numerator += value
            inputs.append(metric)
            found_any = True
        if not found_any:
            warnings.append(f"Missing numerator inputs for {ratio_name} in {period}.")
            return None
    else:
        metric = _first_metric(metrics, spec["numerator"])
        if metric is None:
            warnings.append(f"Missing numerator input for {ratio_name} in {period}.")
            return None
        numerator = _number(metric.get("value"))
        if numerator is None:
            warnings.append(f"Invalid numerator input for {ratio_name} in {period}.")
            return None
        inputs.append(metric)

    denominator_metric = _first_metric(metrics, spec["denominator"])
    if denominator_metric is None:
        warnings.append(f"Missing denominator input for {ratio_name} in {period}.")
        return None
    denominator = _number(denominator_metric.get("value"))
    if denominator is None:
        warnings.append(f"Invalid denominator input for {ratio_name} in {period}.")
        return None
    if denominator == 0:
        warnings.append(f"Zero denominator for {ratio_name} in {period}.")
        return None

    inputs.append(denominator_metric)
    value = numerator / denominator

    return {
        "name": ratio_name,
        "period": period,
        "value": _round(value),
        "unit": spec["unit"],
        "formula": spec["formula"],
        "inputs": [str(metric.get("name")) for metric in inputs],
        "interpretation": f"{ratio_name} computed from structured metrics.",
        "source": "computed",
        "confidence": _min_confidence(inputs),
    }


def _first_metric(
    metrics: dict[str, dict[str, Any]],
    names: list[str],
) -> dict[str, Any] | None:
    for name in names:
        metric = metrics.get(name)
        if metric is not None:
            return metric
    return None


def _comparison_severity(
    metric_name: str,
    base_value: float,
    comparison_value: float,
    percentage_change: float | None,
) -> str:
    if metric_name == "free_cash_flow" and base_value >= 0 and comparison_value < 0:
        return "High"
    if metric_name == "current_ratio" and comparison_value < 1.0:
        return "High" if comparison_value < 0.8 else "Medium"
    if percentage_change is None:
        return "Info"
    if metric_name == "revenue" and percentage_change < -0.15:
        return "High"
    if metric_name == "total_debt" and percentage_change > 0.25:
        return "High" if percentage_change > 0.5 else "Medium"
    abs_change = abs(percentage_change)
    if abs_change > 0.25:
        return "High"
    if abs_change >= 0.10:
        return "Medium"
    return "Low"


def _comparison_explanation(
    metric_name: str,
    base_period: str,
    comparison_period: str,
    absolute_change: float,
    percentage_change: float | None,
    severity: str,
) -> str:
    direction = "increased" if absolute_change >= 0 else "decreased"
    percent_text = (
        "n/a"
        if percentage_change is None
        else f"{percentage_change * 100:.1f}%"
    )
    return (
        f"{metric_name} {direction} from {base_period} to {comparison_period}; "
        f"change={_round(absolute_change)}, percent={percent_text}, severity={severity}."
    )


def _add_ratio_threshold_signal(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    ratio_index: dict[tuple[str, str], dict[str, Any]],
    ratio_name: str,
    threshold: float,
    signal_name: str,
    summary: str,
    severity: str,
    greater_than: bool = False,
) -> None:
    for (_, period), ratio in ratio_index.items():
        if ratio.get("name") != ratio_name:
            continue
        value = _number(ratio.get("value"))
        if value is None:
            continue
        is_triggered = value > threshold if greater_than else value < threshold
        if not is_triggered:
            continue
        item = _evidence_item(
            ratio_name,
            period,
            value,
            threshold,
            ratio.get("unit") or "",
            severity,
            summary,
        )
        _append_signal(signals, evidence, signal_name, severity, period, summary, [item])


def _detect_margin_compression(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    ratio_index: dict[tuple[str, str], dict[str, Any]],
    limitations: list[str],
) -> None:
    margin_ratios = [
        ratio
        for (name, _), ratio in ratio_index.items()
        if name == "ebitda_margin"
    ]
    margin_ratios.sort(key=lambda item: str(item.get("period", "")))
    if len(margin_ratios) < 2:
        limitations.append("Not enough EBITDA margin periods to detect margin compression.")
        return

    previous = margin_ratios[-2]
    current = margin_ratios[-1]
    previous_value = _number(previous.get("value"))
    current_value = _number(current.get("value"))
    if previous_value is None or current_value is None:
        return
    decline = previous_value - current_value
    if decline > 0.05:
        severity = "High" if decline > 0.10 else "Medium"
        item = _evidence_item(
            "ebitda_margin",
            str(current.get("period")),
            current_value,
            previous_value - 0.05,
            "ratio",
            severity,
            "EBITDA margin declined by more than five percentage points.",
        )
        _append_signal(
            signals,
            evidence,
            "MARGIN_COMPRESSION",
            severity,
            str(current.get("period")),
            "EBITDA margin compression requires review.",
            [item],
        )


def _detect_revenue_drop(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    metric_index: dict[tuple[str, str], dict[str, Any]],
) -> None:
    _detect_drop_signal(
        signals,
        evidence,
        metric_index,
        "revenue",
        "REVENUE_DROP",
        0.15,
        "Revenue declined by more than 15%. Human review recommended.",
    )


def _detect_capex_spike(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    metric_index: dict[tuple[str, str], dict[str, Any]],
) -> None:
    capex_metrics = [
        metric
        for (name, _), metric in metric_index.items()
        if name == "capex"
    ]
    capex_metrics.sort(key=lambda item: str(item.get("period", "")))
    if len(capex_metrics) < 2:
        return
    previous = _number(capex_metrics[-2].get("value"))
    current = _number(capex_metrics[-1].get("value"))
    if previous is None or current is None or previous <= 0:
        return
    increase = (current - previous) / previous
    if increase > 0.25:
        period = str(capex_metrics[-1].get("period"))
        item = _evidence_item(
            "capex",
            period,
            current,
            previous * 1.25,
            capex_metrics[-1].get("unit") or "",
            "Medium",
            "Capex increased by more than 25%.",
        )
        _append_signal(
            signals,
            evidence,
            "CAPEX_SPIKE",
            "Medium",
            period,
            "Capex spike should be reviewed against the investment plan.",
            [item],
        )


def _detect_forecast_dependency(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    metric_index: dict[tuple[str, str], dict[str, Any]],
) -> None:
    forecast_periods = {
        period
        for _, period in metric_index
        if str(period).endswith("E")
    }
    actual_periods = {
        period
        for _, period in metric_index
        if str(period).endswith("A")
    }
    if len(forecast_periods) >= 2 and actual_periods:
        period = sorted(forecast_periods)[-1]
        item = _evidence_item(
            "forecast_periods",
            period,
            float(len(forecast_periods)),
            None,
            "count",
            "Info",
            "Analysis includes multiple forecast periods. Forecast assumptions require review.",
        )
        _append_signal(
            signals,
            evidence,
            "FORECAST_DEPENDENCY",
            "Info",
            period,
            "Quantitative analysis depends on forecast periods. Human review recommended.",
            [item],
        )


def _detect_drop_signal(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    metric_index: dict[tuple[str, str], dict[str, Any]],
    metric_name: str,
    signal_name: str,
    threshold: float,
    summary: str,
) -> None:
    metrics = [
        metric
        for (name, _), metric in metric_index.items()
        if name == metric_name
    ]
    metrics.sort(key=lambda item: str(item.get("period", "")))
    if len(metrics) < 2:
        return
    previous = _number(metrics[-2].get("value"))
    current = _number(metrics[-1].get("value"))
    if previous is None or current is None or previous == 0:
        return
    decline = (previous - current) / abs(previous)
    if decline > threshold:
        period = str(metrics[-1].get("period"))
        item = _evidence_item(
            metric_name,
            period,
            current,
            previous * (1 - threshold),
            metrics[-1].get("unit") or "",
            "High",
            summary,
        )
        _append_signal(signals, evidence, signal_name, "High", period, summary, [item])


def _append_signal(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
    name: str,
    severity: str,
    period: str,
    summary: str,
    items: list[dict[str, Any]],
) -> None:
    signals.append(
        {
            "code": name,
            "name": name,
            "severity": severity,
            "period": period,
            "summary": summary,
            "evidence": items,
        }
    )
    evidence.extend(items)


def _evidence_item(
    metric_name: str,
    period: str,
    value: Any,
    threshold: Any,
    unit: str,
    severity: str,
    interpretation: str,
) -> dict[str, Any]:
    return {
        "metricName": metric_name,
        "period": period,
        "value": _round(_number(value)),
        "threshold": _round(_number(threshold)),
        "unit": unit,
        "severity": severity,
        "interpretation": interpretation,
    }


def _number(value: Any) -> float | None:
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _round(value: float | None) -> float | None:
    if value is None:
        return None
    return round(value, 6)


def _min_confidence(items: list[dict[str, Any]]) -> float:
    confidences = [
        _number(item.get("confidence"))
        for item in items
    ]
    resolved = [
        value if value is not None else DEFAULT_CONFIDENCE
        for value in confidences
    ]
    return _round(min(resolved) if resolved else DEFAULT_CONFIDENCE) or DEFAULT_CONFIDENCE


def _distinct(values: list[str]) -> list[str]:
    result: list[str] = []
    for value in values:
        if value not in result:
            result.append(value)
    return result


def _summary_text(
    signals: list[dict[str, Any]],
    evidence: list[dict[str, Any]],
) -> str:
    if not signals and not evidence:
        return "No quantitative risk signals were provided."

    high_count = sum(1 for signal in signals if signal.get("severity") == "High")
    medium_count = sum(1 for signal in signals if signal.get("severity") == "Medium")
    if high_count > 0:
        return (
            f"{high_count} high-severity quantitative risk signal(s) were identified. "
            "Human review recommended."
        )
    if medium_count > 0:
        return (
            f"{medium_count} medium-severity quantitative risk signal(s) were identified. "
            "Human review recommended."
        )
    return "Quantitative evidence was summarized with no high-severity risk signals."
