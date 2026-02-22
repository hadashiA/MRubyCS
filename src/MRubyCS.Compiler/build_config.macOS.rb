MRuby::CrossBuild.new("macos-arm64") do |conf|
  conf.toolchain :clang

  #conf.gem core: 'mruby-compiler'
  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '3baee4b79cd4b6b83e6b72813a15a7b629455aff'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch arm64'
  conf.linker.flags << '-arch arm64'
end

MRuby::CrossBuild.new("macos-x64") do |conf|
  conf.toolchain :clang

  conf.gem github: 'hadashiA/mruby-compiler2',
           checksum_hash: '3baee4b79cd4b6b83e6b72813a15a7b629455aff'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch x86_64'
  conf.linker.flags << '-arch x86_64'
end
