/* > パス: tools/testdata/uwdiff_gen2.c
 * UwDiff 試験データ 第 2 版（2026-10-24）。第 1 版の生成器に足りなかった形を入れる。
 *   1. 1 行で複数セルの修正（1〜7 セル。全部の列が対象）
 *   2. どの列も修正される（Y・測点番号・観測日も）
 *   3. 値 → 空 の修正（空 → 値 もある）
 *   4. 続いた行のまとまった変更（削除・修正・点名変更・追加が数十〜百数十行続く塊）
 *   5. 点名の変更（旧の点名が消え、「改」＋旧の点名が増える → 正解では 削除＋追加）
 *   ほかに、備考に引用符の中の改行を入れる（大きいデータでも塊の境目をまたぐ）。
 * 新ファイルの行順を 3 通り選べる: keep（旧と同じ順）・local（4096 行ごとに並べ替え）・shuffle（全部並べ替え）
 *
 * 使い方:
 *   uwdiff_gen2 count  <目標バイト>                        → 旧の行数 N
 *   uwdiff_gen2 old    <N> <utf8|cp932> <出力>
 *   uwdiff_gen2 new    <N> <utf8|cp932> <keep|local|shuffle> <出力>
 *   uwdiff_gen2 answer <N> <出力>                           → 区分,点名,列,旧値,新値（UTF-8・LF）
 *   uwdiff_gen2 stats  <N>
 * 列: 点名,X,Y,Z,種別,測点番号,観測日,備考   キー列＝点名。UTF-8 は LF、CP932 は CRLF。BOM なし。
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <iconv.h>

#define SEED 0x5577AB1E2026A2ULL
static int CP = 0;

static inline uint64_t mix(uint64_t x){ x+=0x9E3779B97F4A7C15ULL; x=(x^(x>>30))*0xBF58476D1CE4E5B9ULL; x=(x^(x>>27))*0x94D049BB133111EBULL; return x^(x>>31); }
static inline uint64_t H(uint64_t i, uint64_t salt){ return mix(SEED ^ mix(i*0x100000001B3ULL ^ salt)); }

typedef struct { const char *u; char c[160]; int ul, cl; } S;
static S KIND[] = {{"基準点"},{"図根点"},{"引照点"},{"境界杭"},{"水準点"},{"BM"},{"多角点"}};
#define NKIND 7
static S P_KIJUN = {"基準"}, P_KAI = {"改"}, FIX = {"（修正）"};
static S RM[] = {
 {"杭No."}, {" 確認済"}, {" 再測"}, {"ソ表能予 補点"}, {"①～③ 区画"}, {"髙橋氏立会"},
 {"既設、\"移設\"予定, 要確認"}, {"ﾊﾝｶｸ ｶﾅ ﾃｽﾄ"}, {"－12.5 ∥ ≒"}, {"Mar-"}, {"1E"}, {"〒 番地 ２丁目"},
 {"1行目\n2行目 確認"},
};
static void conv(S *s){
  s->ul=(int)strlen(s->u);
  iconv_t cd=iconv_open("CP932","UTF-8"); if(cd==(iconv_t)-1){perror("iconv_open");exit(2);}
  char *in=(char*)s->u,*out=s->c; size_t il=s->ul, ol=sizeof(s->c)-1;
  if(iconv(cd,&in,&il,&out,&ol)==(size_t)-1){fprintf(stderr,"iconv 失敗: %s\n",s->u);exit(2);} *out=0; s->cl=(int)(out-s->c); iconv_close(cd);
}
static void init(void){ for(int i=0;i<NKIND;i++) conv(&KIND[i]); conv(&P_KIJUN); conv(&P_KAI); conv(&FIX); for(unsigned i=0;i<sizeof(RM)/sizeof(RM[0]);i++) conv(&RM[i]); }
static inline char *put(char *p,const S *s){ if(CP){memcpy(p,s->c,s->cl);return p+s->cl;} memcpy(p,s->u,s->ul); return p+s->ul; }
static inline char *putu(char *p,uint64_t v){ char t[24]; int n=0; do{t[n++]='0'+v%10;v/=10;}while(v); while(n) *p++=t[--n]; return p; }
static inline char *putz(char *p,uint64_t v,int w){ for(int k=w-1;k>=0;k--){p[k]='0'+v%10;v/=10;} return p+w; }
static inline char *putfix3(char *p,int64_t v){ if(v<0){*p++='-';v=-v;} p=putu(p,(uint64_t)v/1000); *p++='.'; return putz(p,(uint64_t)v%1000,3); }

/* ---- 行の運命 ---- */
enum { KEEP=0, DEL=1, MOD=2, REN=3, INSRUN=4 };
#define BLK 256
/* 塊: 256 行ごとに 2% の確率で、種類（削除・修正・点名変更・追加）と長さ 1〜128 の連続を置く */
static inline int block_run(uint64_t i, uint64_t *rs, uint64_t *rl){
  uint64_t b=i/BLK, h=H(b,0xB10C);
  if(h%1000>=20) return -1;   /* 2% */
  *rs=b*BLK+(h>>10)%128; *rl=1+(h>>20)%128;
  return (int)((h>>30)%4); /* 0=DEL 1=MOD 2=REN 3=INS */
}
static int fate(uint64_t i){
  uint64_t rs,rl; int t=block_run(i,&rs,&rl);
  if(t>=0 && t<3 && i>=rs && i<rs+rl) return t==0?DEL:(t==1?MOD:REN);
  uint64_t r=H(i,0xFA7E)%1000;
  if(r<3) return DEL;
  if(r<7) return MOD;
  if(r<8) return REN;
  return KEEP;
}
/* 旧の行 i の後ろ（keep の順で）に入る追加行の数 */
static uint64_t inserts_after(uint64_t i){
  uint64_t rs,rl; int t=block_run(i,&rs,&rl);
  uint64_t n=0;
  if(t==3 && i==rs) n+=rl;
  if(H(i,0x1A5E)%1000<3) n+=1;
  return n;
}

/* ---- セル（引用符なしの値）---- */
#define NC 8
static const char *CNAME[NC]={"点名","X","Y","Z","種別","測点番号","観測日","備考"};
static char *c_key(char *p,uint64_t i){ switch(i%5){ case 1: p=put(p,&P_KIJUN); return putu(p,i); case 3: *p++='K'; return putu(p,i); default: return putz(p,i,10);} }
static char *c_added_key(char *p,uint64_t j){ *p++='A'; *p++='D'; return putz(p,j,10); }
static int64_t vx(uint64_t i){ return (int64_t)(H(i,1)%40000000ULL)-20000000; }
static int64_t vy(uint64_t i){ return (int64_t)(H(i,2)%40000000ULL)-20000000; }
static int64_t vz(uint64_t i){ return (int64_t)(H(i,3)%300000ULL); }
static char *c_no(char *p,uint64_t i,int alt){
  uint64_t a=H(i,5),b=H(i,6); char *s=p;
  if(i%4==0){ p=putz(p,0,2); p=putz(p,a%1000000000ULL,9); p=putz(p,b%1000000000ULL,9);} else { p=putz(p,a%10000000000ULL,10); p=putz(p,b%10000000000ULL,10);}
  if(alt){ s[19]= (char)('0'+((s[19]-'0'+1)%10)); }
  return p;
}
static char *c_date(char *p,uint64_t i,int alt){ uint64_t h=H(i,7); uint64_t d=1+(h>>16)%28; if(alt) d=d%28+1; p=putu(p,2015+h%12); *p++='/'; p=putz(p,1+(h>>8)%12,2); *p++='/'; return putz(p,d,2); }
static char *c_rm(char *p,uint64_t i){
  uint64_t h=H(i,8); int t=(int)(h%18); uint64_t a=1+(h>>8)%12, b=1+(h>>16)%28;
  switch(t){
    case 0: case 1: p=put(p,&RM[0]); p=putu(p,a); *p++='-'; p=putu(p,b); return put(p,&RM[1]);
    case 2: p=putu(p,a); *p++='/'; p=putu(p,b); return put(p,&RM[2]);
    case 3: *p++='1'; *p++='-'; return putu(p,b);
    case 4: return put(p,&RM[3]); case 5: return put(p,&RM[4]); case 6: return put(p,&RM[5]);
    case 7: return put(p,&RM[6]); case 8: return put(p,&RM[7]); case 9: return put(p,&RM[8]);
    case 10: p=put(p,&RM[9]); return putu(p,a);
    case 11: p=put(p,&RM[10]); return putu(p,a);
    case 12: return put(p,&RM[11]);
    case 13: return put(p,&RM[12]);
    default: return p; /* 空 */
  }
}
/* 列 c（1..7）の値。how: 0=元 1=別の値 2=空 */
static char *cell(char *p,uint64_t i,int c,int how){
  if(how==2) return p;
  int alt=how==1;
  switch(c){
    case 1: return putfix3(p, vx(i) + (alt? 1+(int64_t)(H(i,11)%999):0));
    case 2: return putfix3(p, vy(i) - (alt? 1+(int64_t)(H(i,12)%999):0));
    case 3: return putfix3(p, vz(i) + (alt?10:0));
    case 4: { int k=(int)(H(i,4)%NKIND); if(alt) k=(k+1)%NKIND; return put(p,&KIND[k]); }
    case 5: return c_no(p,i,alt);
    case 6: return c_date(p,i,alt);
    case 7: { p=c_rm(p,i); if(alt) p=put(p,&FIX); return p; }
  }
  return p;
}
static int rm_empty(uint64_t i){ return H(i,8)%18>=14; }
/* 修正する列の決め方: 列ごとの how（0..2）を mods[1..7] に入れ、変える列の数を返す */
static int modspec(uint64_t i,int mods[NC]){
  for(int c=0;c<NC;c++) mods[c]=0;
  uint64_t h=H(i,0x30D5); int r=(int)(h%100);
  int m = r<45?1 : r<70?2 : r<85?3 : r<92?4 : r<96?5 : r<98?6 : 7;
  int ord[7]={1,2,3,4,5,6,7};
  for(int k=6;k>0;k--){ int j=(int)(H(i,0x5EF+k)%(k+1)); int t=ord[k]; ord[k]=ord[j]; ord[j]=t; }
  for(int k=0;k<m;k++){
    int c=ord[k];
    int how = (H(i,0xE3+c)%5==0) ? 2 : 1;          /* 2 割は 値→空 */
    if(how==2 && c==7 && rm_empty(i)) how=1;        /* 元が空なら 空→値 */
    mods[c]=how;
  }
  return m;
}

/* ---- 行の書き出し（必要なら引用符で囲む）---- */
static char *emit_value(char *p,const char *v,int n){
  int need=0; for(int k=0;k<n;k++){ char ch=v[k]; if(ch==','||ch=='"'||ch=='\n'||ch=='\r'){need=1;break;} }
  if(!need){ memcpy(p,v,n); return p+n; }
  *p++='"'; for(int k=0;k<n;k++){ if(v[k]=='"') *p++='"'; *p++=v[k]; } *p++='"'; return p;
}
static char *eol(char *p){ if(CP) *p++='\r'; *p++='\n'; return p; }
/* kind: 0=旧 1=新（運命を反映） 2=追加行 j */
static char *row(char *p,uint64_t i,int kind){
  char v[1024]; char *e;
  int mods[NC]={0}; int f = kind==1? fate(i):KEEP;
  if(f==MOD) modspec(i,mods);
  /* 点名 */
  if(kind==2) e=c_added_key(v,i);
  else if(f==REN){ e=put(v,&P_KAI); e=c_key(e,i); }
  else e=c_key(v,i);
  p=emit_value(p,v,(int)(e-v));
  for(int c=1;c<NC;c++){
    *p++=',';
    uint64_t src = kind==2 ? (i*0x9E37ULL+77) : i;   /* 追加行の中身は別の番号から作る */
    e=cell(v,src,c,mods[c]);
    p=emit_value(p,v,(int)(e-v));
  }
  return eol(p);
}
static char *header(char *p){
  for(int c=0;c<NC;c++){ if(c) *p++=','; S s={CNAME[c]}; conv(&s); p=put(p,&s); }
  return eol(p);
}

#define BUF (16<<20)
static char *buf,*bp; static FILE *fo;
static inline void flush_if(void){ if(bp-buf>BUF-65536){ fwrite(buf,1,bp-buf,fo); bp=buf; } }
static void openout(const char *path){ fo=fopen(path,"wb"); if(!fo){perror(path);exit(1);} buf=malloc(BUF); bp=buf; }
static void closeout(void){ fwrite(buf,1,bp-buf,fo); if(fclose(fo)){perror("fclose");exit(1);} }

static uint64_t rs_=0x2026102402ULL;
static inline uint64_t rnd(void){ rs_+=0x9E3779B97F4A7C15ULL; return mix(rs_); }

/* 新の行の並び（keep の順）: 旧 i（削除以外）と、その後ろに追加行。値: i（< N）または N + j */
static uint64_t *build_order(uint64_t N,uint64_t *M){
  uint64_t cap=N+N/50+1024, n=0, j=0;
  uint64_t *a=malloc(sizeof(uint64_t)*cap); if(!a){fprintf(stderr,"メモリ不足\n");exit(1);}
  for(uint64_t i=0;i<N;i++){
    if(fate(i)!=DEL){ if(n==cap){cap*=2;a=realloc(a,sizeof(uint64_t)*cap);} a[n++]=i; }
    uint64_t k=inserts_after(i);
    for(uint64_t t=0;t<k;t++){ if(n==cap){cap*=2;a=realloc(a,sizeof(uint64_t)*cap);} a[n++]=N+(j++); }
  }
  *M=n; return a;
}

static void wr_cell_utf8(int c,uint64_t i,int how){ char v[1024]; char *e=cell(v,i,c,how); bp=emit_value(bp,v,(int)(e-v)); }

int main(int argc,char **argv){
  if(argc<3){fprintf(stderr,"usage: see source\n");return 1;}
  init();
  const char *mode=argv[1];
  if(!strcmp(mode,"count")){
    double target=atof(argv[2]); char t[4096]; uint64_t tot=0,n=200000;
    for(uint64_t i=0;i<n;i++){ char *e=row(t,i*7919+3,0); tot+=e-t; }
    printf("%llu\n",(unsigned long long)(target/((double)tot/n))); return 0;
  }
  uint64_t N=strtoull(argv[2],0,10);
  if(!strcmp(mode,"stats")){
    uint64_t d=0,m=0,r=0,cells=0,ins=0,runs[4]={0},runrows[4]={0}; int mods[NC];
    uint64_t hist[8]={0}, colc[NC]={0}, empt=0;
    for(uint64_t i=0;i<N;i++){
      int f=fate(i); if(f==DEL)d++; else if(f==REN)r++; else if(f==MOD){ m++; int k=modspec(i,mods); cells+=k; hist[k]++; for(int c=1;c<NC;c++){ if(mods[c]) colc[c]++; if(mods[c]==2) empt++; } }
      ins+=inserts_after(i);
      if(i%BLK==0){ uint64_t rs,rl; int t=block_run(i,&rs,&rl); if(t>=0){ runs[t]++; uint64_t e=rs+rl; if(e>N) e=N; if(e>rs) runrows[t]+=e-rs; } }
    }
    printf("rows_old=%llu deleted=%llu modified=%llu modified_cells=%llu renamed=%llu added=%llu rows_new=%llu\n",
      (unsigned long long)N,(unsigned long long)d,(unsigned long long)m,(unsigned long long)cells,(unsigned long long)r,(unsigned long long)ins,(unsigned long long)(N-d+ins));
    printf("cells_per_modified_row:"); for(int k=1;k<=7;k++) printf(" %d=%llu",k,(unsigned long long)hist[k]); printf("\n");
    printf("modified_by_column:"); for(int c=1;c<NC;c++) printf(" %s=%llu",CNAME[c],(unsigned long long)colc[c]); printf(" to_empty=%llu\n",(unsigned long long)empt);
    printf("block_runs: del=%llu(%llu rows) mod=%llu(%llu) ren=%llu(%llu) ins=%llu(%llu)\n",
      (unsigned long long)runs[0],(unsigned long long)runrows[0],(unsigned long long)runs[1],(unsigned long long)runrows[1],
      (unsigned long long)runs[2],(unsigned long long)runrows[2],(unsigned long long)runs[3],(unsigned long long)runrows[3]);
    return 0;
  }
  if(!strcmp(mode,"old")){
    CP=!strcmp(argv[3],"cp932"); openout(argv[4]); bp=header(bp);
    for(uint64_t i=0;i<N;i++){ bp=row(bp,i,0); flush_if(); }
    closeout(); return 0;
  }
  if(!strcmp(mode,"new")){
    CP=!strcmp(argv[3],"cp932"); const char *ord=argv[4];
    uint64_t M; uint64_t *a=build_order(N,&M);
    if(!strcmp(ord,"shuffle")){ for(uint64_t k=M-1;k>0;k--){ uint64_t j=rnd()%(k+1); uint64_t t=a[k];a[k]=a[j];a[j]=t; } }
    else if(!strcmp(ord,"local")){ for(uint64_t s=0;s<M;s+=4096){ uint64_t e=s+4096<M?s+4096:M; for(uint64_t k=e-1;k>s;k--){ uint64_t j=s+rnd()%(k-s+1); uint64_t t=a[k];a[k]=a[j];a[j]=t; } } }
    else if(strcmp(ord,"keep")){ fprintf(stderr,"順は keep・local・shuffle\n"); return 1; }
    openout(argv[5]); bp=header(bp);
    for(uint64_t k=0;k<M;k++){ uint64_t v=a[k]; bp = v<N ? row(bp,v,1) : row(bp,v-N,2); flush_if(); }
    closeout(); free(a); return 0;
  }
  if(!strcmp(mode,"answer")){
    CP=0; openout(argv[3]);
    { const char *h="区分,点名,列,旧値,新値\n"; memcpy(bp,h,strlen(h)); bp+=strlen(h); }
    char k1[256]; char *ke; uint64_t j=0; int mods[NC];
    for(uint64_t i=0;i<N;i++){
      int f=fate(i);
      ke=c_key(k1,i);
      if(f==DEL||f==REN){ memcpy(bp,"削除,",strlen("削除,")); bp+=strlen("削除,"); bp=emit_value(bp,k1,(int)(ke-k1)); memcpy(bp,",,,\n",4); bp+=4; }
      if(f==REN){ char k2[256]; char *e2=put(k2,&P_KAI); e2=c_key(e2,i); memcpy(bp,"追加,",strlen("追加,")); bp+=strlen("追加,"); bp=emit_value(bp,k2,(int)(e2-k2)); memcpy(bp,",,,\n",4); bp+=4; }
      if(f==MOD){
        modspec(i,mods);
        for(int c=1;c<NC;c++){
          if(!mods[c]) continue;
          memcpy(bp,"変更,",strlen("変更,")); bp+=strlen("変更,");
          bp=emit_value(bp,k1,(int)(ke-k1)); *bp++=',';
          bp=emit_value(bp,CNAME[c],(int)strlen(CNAME[c])); *bp++=',';
          wr_cell_utf8(c,i,0); *bp++=',';
          wr_cell_utf8(c,i,mods[c]); *bp++='\n';
        }
      }
      uint64_t k=inserts_after(i);
      for(uint64_t t=0;t<k;t++){ char k3[64]; char *e3=c_added_key(k3,j++); memcpy(bp,"追加,",strlen("追加,")); bp+=strlen("追加,"); bp=emit_value(bp,k3,(int)(e3-k3)); memcpy(bp,",,,\n",4); bp+=4; }
      flush_if();
    }
    closeout(); return 0;
  }
  fprintf(stderr,"不明な mode\n"); return 1;
}
