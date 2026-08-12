"""
Sinh tests/jmeter/L3-PERF.jmx cho sheet L3-Performance (Report_5_3 v1.3).

Vì sao sinh bằng script thay vì viết tay XML: mỗi Thread Group của JMeter là ~60 dòng XML lặp lại;
9 case viết tay vừa dài vừa dễ lệch cấu hình giữa các case. Script giữ CẤU HÌNH ở một chỗ (bảng CASES)
nên đọc/soát nhanh và sửa ngưỡng chỉ ở một dòng.

Workbook ghi công cụ là k6. Nhóm dùng JMeter thay thế (Java 21 có sẵn trên máy chạy kiểm thử,
không phải cài thêm runtime). NGƯỠNG NFR giữ NGUYÊN không đổi.

Chạy:  python tests/jmeter/build_jmx.py
"""
import io
import os

HOST, PORT = "localhost", "5080"

# Rút ngắn thời lượng so với workbook để cả 9 case chạy gọn trong một cửa sổ kiểm thử.
# SỐ LUỒNG (VUs) và NGƯỠNG giữ ĐÚNG như workbook — đó mới là phần quyết định kết quả p95.
# Lệch này được ghi rõ ở cột Notes của bảng kết quả.
CASES = [
    # (id, tên, VUs, ramp(s), duration(s), ngưỡng p95 ms, method, path, body, cần token)
    ("L3-PERF-01", "GET /api/products (catalogue)",
     20, 30, 60, 500, "GET", "/api/products", None, None),
    ("L3-PERF-02", "GET /api/products co tim kiem + phan trang",
     20, 15, 60, 800, "GET", "/api/products?search=bang&page=1&pageSize=12", None, None),
    ("L3-PERF-03", "GET /api/orders/checkout-summary",
     10, 10, 60, 1000, "GET", "/api/orders/checkout-summary", None, "customer"),
    # PERF-04 (SignalR ChatHub) KHÔNG có ở đây: JMeter không nói được WebSocket/SignalR nếu không
    # cài plugin ngoài. Xem cột Notes của bảng kết quả — case này đánh Blocked kèm lý do.
    ("L3-PERF-05", "POST /api/webhooks/sepay-callback (ack < 3s)",
     30, 15, 60, 3000, "POST", "/api/webhooks/sepay-callback",
     '{"gateway":"TPBank","accountNumber":"0000000000","transferAmount":1,'
     '"transferContent":"VT-PERF-NOMATCH","content":"VT-PERF-NOMATCH",'
     '"referenceCode":"REF-PERF-05","referenceNumber":"REF-PERF-05"}', "sepay"),
    ("L3-PERF-06", "GET /api/dashboards/sales-staff",
     10, 10, 60, 3000, "GET", "/api/dashboards/sales-staff", None, "sales"),
    ("L3-PERF-07", "GET /api/marketing-posts (phan hoi dau < 1s)",
     5, 5, 60, 1000, "GET", "/api/marketing-posts", None, "sales"),
    ("L3-PERF-08", "STRESS hon hop 100 VUs",
     100, 30, 90, 5000, "GET", "/api/products", None, None),
    ("L3-PERF-09", "GET /api/orders/my-history (truy van lich su)",
     10, 10, 60, 3000, "GET", "/api/orders/my-history", None, "customer"),
]


def esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
             .replace('"', "&quot;"))


def login_sampler(name, email, prop_name):
    """Sampler đăng nhập + JSR223 lưu accessToken vào property toàn cục cho các Thread Group sau."""
    return f'''
        <HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="{esc(name)}" enabled="true">
          <elementProp name="HTTPsampler.Arguments" elementType="Arguments">
            <collectionProp name="Arguments.arguments">
              <elementProp name="" elementType="HTTPArgument">
                <boolProp name="HTTPArgument.always_encode">false</boolProp>
                <stringProp name="Argument.value">{{&quot;email&quot;:&quot;{email}&quot;,&quot;password&quot;:&quot;123456&quot;}}</stringProp>
                <stringProp name="Argument.metadata">=</stringProp>
              </elementProp>
            </collectionProp>
          </elementProp>
          <stringProp name="HTTPSampler.domain">{HOST}</stringProp>
          <stringProp name="HTTPSampler.port">{PORT}</stringProp>
          <stringProp name="HTTPSampler.protocol">http</stringProp>
          <stringProp name="HTTPSampler.path">/api/auth/login</stringProp>
          <stringProp name="HTTPSampler.method">POST</stringProp>
          <boolProp name="HTTPSampler.postBodyRaw">true</boolProp>
        </HTTPSamplerProxy>
        <hashTree>
          <HeaderManager guiclass="HeaderPanel" testclass="HeaderManager" testname="JSON" enabled="true">
            <collectionProp name="HeaderManager.headers">
              <elementProp name="" elementType="Header">
                <stringProp name="Header.name">Content-Type</stringProp>
                <stringProp name="Header.value">application/json</stringProp>
              </elementProp>
            </collectionProp>
          </HeaderManager>
          <hashTree/>
          <JSR223PostProcessor guiclass="TestBeanGUI" testclass="JSR223PostProcessor" testname="Luu token" enabled="true">
            <stringProp name="scriptLanguage">groovy</stringProp>
            <stringProp name="script">
def json = new groovy.json.JsonSlurper().parseText(prev.getResponseDataAsString())
props.put("{prop_name}", json.data.accessToken)
log.info("Da luu {prop_name}")
            </stringProp>
          </JSR223PostProcessor>
          <hashTree/>
        </hashTree>'''


def thread_group(case):
    tid, name, vus, ramp, dur, threshold, method, path, body, auth = case

    if auth == "customer":
        header = ('<elementProp name="" elementType="Header">'
                  '<stringProp name="Header.name">Authorization</stringProp>'
                  '<stringProp name="Header.value">Bearer ${__P(customerToken)}</stringProp>'
                  '</elementProp>')
    elif auth == "sales":
        header = ('<elementProp name="" elementType="Header">'
                  '<stringProp name="Header.name">Authorization</stringProp>'
                  '<stringProp name="Header.value">Bearer ${__P(salesToken)}</stringProp>'
                  '</elementProp>')
    elif auth == "sepay":
        header = ('<elementProp name="" elementType="Header">'
                  '<stringProp name="Header.name">x-sepay-token</stringProp>'
                  '<stringProp name="Header.value">test-sepay-token-not-a-real-secret</stringProp>'
                  '</elementProp>')
    else:
        header = ""

    header += ('<elementProp name="" elementType="Header">'
               '<stringProp name="Header.name">Content-Type</stringProp>'
               '<stringProp name="Header.value">application/json</stringProp>'
               '</elementProp>')

    if body:
        body_xml = f'''<boolProp name="HTTPSampler.postBodyRaw">true</boolProp>
          <elementProp name="HTTPsampler.Arguments" elementType="Arguments">
            <collectionProp name="Arguments.arguments">
              <elementProp name="" elementType="HTTPArgument">
                <boolProp name="HTTPArgument.always_encode">false</boolProp>
                <stringProp name="Argument.value">{esc(body)}</stringProp>
                <stringProp name="Argument.metadata">=</stringProp>
              </elementProp>
            </collectionProp>
          </elementProp>'''
    else:
        body_xml = '''<elementProp name="HTTPsampler.Arguments" elementType="Arguments">
            <collectionProp name="Arguments.arguments"/>
          </elementProp>'''

    if "?" in path:
        raw_path, query = path.split("?", 1)
        full_path = f"{raw_path}?{esc(query)}"
    else:
        full_path = path

    return f'''
      <ThreadGroup guiclass="ThreadGroupGui" testclass="ThreadGroup" testname="{esc(tid)} — {esc(name)}" enabled="true">
        <stringProp name="ThreadGroup.on_sample_error">continue</stringProp>
        <elementProp name="ThreadGroup.main_controller" elementType="LoopController">
          <boolProp name="LoopController.continue_forever">true</boolProp>
          <stringProp name="LoopController.loops">-1</stringProp>
        </elementProp>
        <stringProp name="ThreadGroup.num_threads">{vus}</stringProp>
        <stringProp name="ThreadGroup.ramp_time">{ramp}</stringProp>
        <boolProp name="ThreadGroup.scheduler">true</boolProp>
        <stringProp name="ThreadGroup.duration">{dur}</stringProp>
        <stringProp name="ThreadGroup.delay">0</stringProp>
      </ThreadGroup>
      <hashTree>
        <HTTPSamplerProxy guiclass="HttpTestSampleGui" testclass="HTTPSamplerProxy" testname="{esc(tid)}" enabled="true">
          {body_xml}
          <stringProp name="HTTPSampler.domain">{HOST}</stringProp>
          <stringProp name="HTTPSampler.port">{PORT}</stringProp>
          <stringProp name="HTTPSampler.protocol">http</stringProp>
          <stringProp name="HTTPSampler.path">{full_path}</stringProp>
          <stringProp name="HTTPSampler.method">{method}</stringProp>
          <boolProp name="HTTPSampler.follow_redirects">true</boolProp>
          <boolProp name="HTTPSampler.use_keepalive">true</boolProp>
          <stringProp name="HTTPSampler.connect_timeout">10000</stringProp>
          <stringProp name="HTTPSampler.response_timeout">30000</stringProp>
        </HTTPSamplerProxy>
        <hashTree>
          <HeaderManager guiclass="HeaderPanel" testclass="HeaderManager" testname="Headers" enabled="true">
            <collectionProp name="HeaderManager.headers">{header}</collectionProp>
          </HeaderManager>
          <hashTree/>
          <DurationAssertion guiclass="DurationAssertionGui" testclass="DurationAssertion" testname="Nguong {threshold}ms" enabled="true">
            <stringProp name="DurationAssertion.duration">{threshold}</stringProp>
          </DurationAssertion>
          <hashTree/>
        </hashTree>
      </hashTree>'''


def build():
    groups = "".join(thread_group(c) for c in CASES)

    setup = f'''
      <SetupThreadGroup guiclass="SetupThreadGroupGui" testclass="SetupThreadGroup" testname="setUp — lay JWT" enabled="true">
        <stringProp name="ThreadGroup.on_sample_error">stopthread</stringProp>
        <elementProp name="ThreadGroup.main_controller" elementType="LoopController">
          <boolProp name="LoopController.continue_forever">false</boolProp>
          <stringProp name="LoopController.loops">1</stringProp>
        </elementProp>
        <stringProp name="ThreadGroup.num_threads">1</stringProp>
        <stringProp name="ThreadGroup.ramp_time">1</stringProp>
        <boolProp name="ThreadGroup.scheduler">false</boolProp>
      </SetupThreadGroup>
      <hashTree>{login_sampler("Login Customer", "customer.test@viettien.com", "customerToken")}
        {login_sampler("Login Sales Staff", "salesstaff.test@viettien.com", "salesToken")}
      </hashTree>'''

    xml = f'''<?xml version="1.0" encoding="UTF-8"?>
<jmeterTestPlan version="1.2" properties="5.0" jmeter="5.6.3">
  <hashTree>
    <TestPlan guiclass="TestPlanGui" testclass="TestPlan" testname="L3-Performance — Report_5_3 v1.3" enabled="true">
      <stringProp name="TestPlan.comments">Ngưỡng NFR-P01..P07, S01, S02 lấy từ workbook. JMeter thay cho k6; ngưỡng giữ nguyên.</stringProp>
      <boolProp name="TestPlan.functional_mode">false</boolProp>
      <boolProp name="TestPlan.serialize_threadgroups">true</boolProp>
      <elementProp name="TestPlan.user_defined_variables" elementType="Arguments" guiclass="ArgumentsPanel" testclass="Arguments">
        <collectionProp name="Arguments.arguments"/>
      </elementProp>
    </TestPlan>
    <hashTree>{setup}{groups}
    </hashTree>
  </hashTree>
</jmeterTestPlan>
'''
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "L3-PERF.jmx")
    io.open(out, "w", encoding="utf-8").write(xml)
    print("Da sinh:", out)
    print("So Thread Group:", len(CASES))


if __name__ == "__main__":
    build()
